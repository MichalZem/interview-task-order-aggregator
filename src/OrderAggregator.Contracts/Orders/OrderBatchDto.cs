namespace OrderAggregator.Contracts.Orders;

/// <summary>
/// Outbound aggregated batch — JSON shape handed to the downstream system
/// by every <c>IAggregatedOrderSender</c> implementation.
/// </summary>
/// <param name="Orders">Aggregated rows, one per distinct productId in the window.</param>
/// <param name="FlushedAt">Timestamp when the batch was flushed to the downstream system.</param>
public sealed record OrderBatchDto(
    IReadOnlyCollection<AggregatedOrderDto> Orders,
    DateTimeOffset FlushedAt);
