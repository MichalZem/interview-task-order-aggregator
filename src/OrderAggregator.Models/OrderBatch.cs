namespace OrderAggregator.Models;

/// <summary>
/// One flushed aggregation window. <see cref="BatchId"/> is a stable idempotency key
/// generated once when the batch is created; it round-trips through the dead-letter
/// file and every (re)send, so a downstream that deduplicates on it turns the
/// at-least-once delivery (flush retries, replay resends) into effectively exactly-once.
/// </summary>
public sealed record OrderBatch(
    Guid BatchId,
    IReadOnlyCollection<AggregatedOrder> Orders,
    DateTimeOffset FlushedAt);
