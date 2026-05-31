using System.Diagnostics.Metrics;

namespace OrderAggregator.Services.Diagnostics;

/// <summary>
/// Domain metric instruments for the aggregation pipeline. Registered as a
/// singleton and shared by the ingest endpoint (inbound) and the flush loop
/// (outbound), so a single object owns the <see cref="Meter"/> lifetime.
/// The OpenTelemetry meter provider subscribes by <see cref="MeterName"/>.
/// </summary>
public sealed class OrderAggregationMetrics : IDisposable
{
    /// <summary>Meter name the OpenTelemetry provider subscribes to via <c>AddMeter(...)</c>.</summary>
    public const string MeterName = "OrderAggregator.OrderAggregation";

    private readonly Meter _meter;

    // Inbound — what clients push at us.
    private readonly Counter<long> _ordersAccepted;
    private readonly Counter<long> _batchesRejected;

    // Outbound — health of the 20s flush loop.
    private readonly Histogram<int> _flushBatchSize;
    private readonly Histogram<double> _flushDuration;
    private readonly Counter<long> _flushSent;
    private readonly Counter<long> _flushDeadLettered;

    public OrderAggregationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _ordersAccepted = _meter.CreateCounter<long>(
            "orderaggregator.orders.accepted",
            unit: "{order}",
            description: "Individual orders accepted into the aggregation buffer.");

        _batchesRejected = _meter.CreateCounter<long>(
            "orderaggregator.orders.rejected_batches",
            unit: "{batch}",
            description: "Ingest requests rejected by validation (unknown productId, bad payload).");

        _flushBatchSize = _meter.CreateHistogram<int>(
            "orderaggregator.flush.batch_size",
            unit: "{product}",
            description: "Distinct products carried by each flushed aggregated batch.");

        _flushDuration = _meter.CreateHistogram<double>(
            "orderaggregator.flush.duration",
            unit: "ms",
            description: "Wall-clock duration of a single flush (snapshot + downstream send).");

        _flushSent = _meter.CreateCounter<long>(
            "orderaggregator.flush.sent",
            unit: "{batch}",
            description: "Aggregated batches successfully delivered downstream.");

        _flushDeadLettered = _meter.CreateCounter<long>(
            "orderaggregator.flush.dead_lettered",
            unit: "{batch}",
            description: "Aggregated batches dead-lettered after exhausting send retries.");
    }

    public void RecordOrdersAccepted(int count) => _ordersAccepted.Add(count);

    public void RecordBatchRejected() => _batchesRejected.Add(1);

    /// <summary>Record the outcome of one flush: batch shape, duration, and success/failure.</summary>
    public void RecordFlush(int productCount, double durationMs, bool delivered)
    {
        _flushBatchSize.Record(productCount);
        _flushDuration.Record(durationMs);
        if (delivered)
        {
            _flushSent.Add(1);
        }
        else
        {
            _flushDeadLettered.Add(1);
        }
    }

    public void Dispose() => _meter.Dispose();
}
