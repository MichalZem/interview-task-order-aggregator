namespace OrderAggregator.Contracts.Orders;

/// <summary>
/// Outbound aggregated batch — JSON shape handed to the downstream system
/// by every <c>IAggregatedOrderSender</c> implementation.
/// </summary>
/// <param name="BatchId">Stable idempotency key for this batch. The same id is sent on every
/// retry and dead-letter replay, so a downstream that deduplicates on it never double-counts.</param>
/// <param name="Orders">Aggregated rows, one per distinct productId in the window.</param>
/// <param name="FlushedAt">Timestamp when the batch was flushed to the downstream system.</param>
public sealed record OrderBatchDto(
    Guid BatchId,
    IReadOnlyCollection<AggregatedOrderDto> Orders,
    DateTimeOffset FlushedAt);
