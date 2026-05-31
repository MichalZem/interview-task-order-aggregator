using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderAggregator.Services.Diagnostics;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Api.DependencyInjection;

/// <summary>
/// OpenTelemetry composition: traces, metrics, and logs exported over OTLP to a
/// local Aspire Dashboard (or any OTLP-compatible collector). Auto-instrumentation
/// (ASP.NET Core, HttpClient, runtime) is combined with the domain's own
/// <see cref="OrderAggregationMetrics"/> meter and aggregation
/// <see cref="OrderAggregationDiagnostics.ActivitySource"/>.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddAppObservability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(ObservabilityOptions.SectionName)
            .Get<ObservabilityOptions>() ?? new ObservabilityOptions();

        // Master switch: with no providers registered, the Meter/ActivitySource
        // still exist but no one listens, so instrumentation costs ~nothing.
        if (!options.Enabled)
        {
            return services;
        }

        var otlpEndpoint = new Uri(options.OtlpEndpoint);

        // service.name / service.instance.id — the resource identity every signal
        // is tagged with, and what the dashboard's resource picker shows.
        void AddServiceResource(ResourceBuilder resource) => resource.AddService(
            serviceName: options.ServiceName,
            serviceInstanceId: Environment.MachineName);

        // Route ILogger output through OpenTelemetry so logs land in the same
        // backend and correlate (trace_id/span_id) with the traces above.
        services.AddLogging(logging => logging.AddOpenTelemetry(otel =>
        {
            otel.IncludeFormattedMessage = true;
            otel.IncludeScopes = true;

            var loggerResource = ResourceBuilder.CreateDefault();
            AddServiceResource(loggerResource);
            otel.SetResourceBuilder(loggerResource);
        }));

        services.AddOpenTelemetry()
            .ConfigureResource(AddServiceResource)
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource(OrderAggregationDiagnostics.ActivitySourceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(OrderAggregationMetrics.MeterName))
            // OTLP for all three signals. OTEL_EXPORTER_OTLP_ENDPOINT, if set,
            // overrides the configured endpoint.
            .UseOtlpExporter(OtlpExportProtocol.Grpc, otlpEndpoint);

        return services;
    }
}
