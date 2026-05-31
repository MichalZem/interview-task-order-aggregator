namespace OrderAggregator.Contracts.Orders;

/// <summary>
/// One aggregated row published in an <see cref="OrderBatchDto"/>.
/// </summary>
/// <param name="ProductId">Identifier of the aggregated product.</param>
/// <param name="Quantity">Total quantity summed across all orders for this product in the flush window.</param>
public sealed record AggregatedOrderDto(
    string ProductId,
    long Quantity);
