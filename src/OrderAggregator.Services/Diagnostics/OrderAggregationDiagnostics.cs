using System.Diagnostics;

namespace OrderAggregator.Services.Diagnostics;

/// <summary>
/// Tracing entry point for the aggregation domain. The flush loop runs on a
/// background timer, not inside an HTTP request, so ASP.NET Core auto-
/// instrumentation never sees it — without this <see cref="ActivitySource"/>
/// the single most important operation would be invisible in traces.
/// </summary>
public static class OrderAggregationDiagnostics
{
    /// <summary>
    /// Source name the OpenTelemetry tracer subscribes to via
    /// <c>AddSource(...)</c>. Keep in sync with the DI registration.
    /// </summary>
    public const string ActivitySourceName = "OrderAggregator.OrderAggregation";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
