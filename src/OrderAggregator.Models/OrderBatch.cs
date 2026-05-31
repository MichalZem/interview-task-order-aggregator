namespace OrderAggregator.Models;

public sealed record OrderBatch(
    IReadOnlyCollection<AggregatedOrder> Orders,
    DateTimeOffset FlushedAt);
