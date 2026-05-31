using Microsoft.AspNetCore.Authentication;

namespace OrderAggregator.Api.Authentication;

public static class ApiKeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers API-key authentication using the <c>ApiKey</c> configuration
    /// section and creates a default authorization policy that requires an
    /// authenticated user. Endpoints opt out via <c>.AllowAnonymous()</c>.
    /// </summary>
    public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection(ApiKeyOptions.SectionName))
            .ValidateDataAnnotations()
            // ValidateDataAnnotations does not recurse into collection items, so
            // the [StringLength] on ApiKeyEntry.Key never runs. Enforce the same
            // rules here explicitly, including the >= 8 char minimum, so weak keys
            // fail at startup instead of silently being accepted.
            .Validate(o => o.Keys.All(k => !string.IsNullOrWhiteSpace(k.Name)),
                "Each ApiKey entry must define a non-empty Name.")
            .Validate(o => o.Keys.All(k => !string.IsNullOrWhiteSpace(k.Key) && k.Key.Trim().Length >= 8),
                "Each ApiKey entry must define a Key of at least 8 characters.")
            .ValidateOnStart();

        services
            .AddAuthentication(ApiKeyAuthenticationDefaults.AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme,
                _ => { });

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(ApiKeyAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }
}
