using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OrderAggregator.Api.Authentication;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Adds the <c>ApiKey</c> security scheme to the OpenAPI <c>components</c>
/// section and applies it as a requirement on every operation that the
/// <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute"/>-aware
/// metadata marks as requiring authorization.
/// </summary>
/// <remarks>
/// Swagger UI and Scalar both honor this — Swagger UI shows an "Authorize"
/// button, Scalar adds the key to its auth panel and to generated client
/// snippets.
/// </remarks>
public sealed class ApiKeySecuritySchemeDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly IOptionsMonitor<ApiKeyOptions> _apiKeyOptions;

    public ApiKeySecuritySchemeDocumentTransformer(IOptionsMonitor<ApiKeyOptions> apiKeyOptions)
    {
        _apiKeyOptions = apiKeyOptions;
    }

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var headerName = _apiKeyOptions.CurrentValue.HeaderName;

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = headerName,
            Description =
                $"Send a configured API key in the `{headerName}` header. " +
                "Keys are managed in the `ApiKey:Keys` section of appsettings (multiple keys are supported).",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[ApiKeyAuthenticationDefaults.AuthenticationScheme] = scheme;

        var schemeReference = new OpenApiSecuritySchemeReference(
            ApiKeyAuthenticationDefaults.AuthenticationScheme,
            document,
            externalResource: null!);

        // Apply the requirement on every operation that ASP.NET Core marked
        // as needing authorization. Anonymous operations (health, OpenAPI doc,
        // Scalar landing) are left untouched.
        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        foreach (var (_, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null)
            {
                continue;
            }

            foreach (var (_, operation) in pathItem.Operations)
            {
                if (!RequiresAuthorization(operation))
                {
                    continue;
                }

                operation.Security ??= new List<OpenApiSecurityRequirement>();
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [schemeReference] = new List<string>(),
                });
            }
        }

        return Task.CompletedTask;
    }

    private static bool RequiresAuthorization(OpenApiOperation operation)
    {
        // Easiest heuristic: any operation declaring a 401 response gates on
        // the API key. We emit 401 explicitly via .ProducesProblem in the
        // endpoint metadata for exactly this reason.
        return operation.Responses?.ContainsKey("401") == true;
    }
}
