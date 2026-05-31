using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using OrderAggregator.Shared.Const;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Adds an optional <c>culture</c> query parameter (an enum of the supported
/// cultures) to every authorized operation, so the Swagger UI / Scalar "Try it
/// out" panel offers a language dropdown. Picking a value sends <c>?culture=cs</c>,
/// which the request-localization query provider reads to switch
/// <c>CurrentUICulture</c> — letting a reviewer see localized error messages
/// without crafting an Accept-Language header by hand.
/// </summary>
public sealed class CultureParameterDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
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
                // Same heuristic as the API-key transformer: a declared 401 marks
                // the business endpoints. Anonymous ones (health, OpenAPI doc) are
                // left clean.
                if (operation.Responses?.ContainsKey("401") != true)
                {
                    continue;
                }

                operation.Parameters ??= [];
                operation.Parameters.Add(BuildCultureParameter());
            }
        }

        return Task.CompletedTask;
    }

    private static OpenApiParameter BuildCultureParameter() => new()
    {
        Name = "culture",
        In = ParameterLocation.Query,
        Required = false,
        Description =
            "Language of the response (error messages). Maps to `?culture=` and switches " +
            "CurrentUICulture. Defaults to the neutral culture when omitted.",
        Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            // Enum + default come straight from the configured cultures, so the
            // dropdown can never drift from what the middleware actually accepts.
            Enum = [.. LocalizationConstants.SupportedCultures.Select(c => (JsonNode)JsonValue.Create(c))],
            Default = JsonValue.Create(LocalizationConstants.DefaultCulture),
        },
    };
}
