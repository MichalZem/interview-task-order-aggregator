namespace OrderAggregator.Models;

public sealed record AggregatedOrder(
    string ProductId,
    long Quantity);
