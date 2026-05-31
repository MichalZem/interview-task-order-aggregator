using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// OpenTelemetry wiring. Bound from configuration section <c>Observability</c>.
/// Defaults target a locally running Aspire Dashboard (OTLP/gRPC on 4317).
/// </summary>
public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>
    /// Master switch. When false, no OpenTelemetry providers are registered at
    /// all — handy for the test host and for environments without a collector.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Logical service name attached to every span/metric/log as
    /// <c>service.name</c>. This is what shows up as the resource in the
    /// dashboard's resource picker.
    /// </summary>
    [Required]
    public string ServiceName { get; set; } = "OrderAggregator.Api";

    /// <summary>
    /// OTLP collector endpoint. The Aspire Dashboard's standalone container
    /// listens on gRPC :4317 by default. The standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variable, if set, takes
    /// precedence over this value.
    /// </summary>
    [Required]
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
