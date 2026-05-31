using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace OrderAggregator.Api.Health;

public static class HealthChecksExtensions
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();

        return services;
    }

    public static WebApplication MapHealthChecks(this WebApplication app)
    {
        var livenessOptions = new HealthCheckOptions { Predicate = static _ => false };
        var readinessOptions = new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains(ReadyTag),
        };

        if (app.Environment.IsDevelopment())
        {
            livenessOptions.ResponseWriter = HealthCheckResponseWriter.WriteDetailedJson;
            readinessOptions.ResponseWriter = HealthCheckResponseWriter.WriteDetailedJson;
        }

        app.MapHealthChecks("/health/live", livenessOptions).AllowAnonymous();
        app.MapHealthChecks("/health/ready", readinessOptions).AllowAnonymous();

        return app;
    }
}
