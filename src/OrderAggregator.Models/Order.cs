namespace OrderAggregator.Models;

public sealed record Order(
    string ProductId,
    int Quantity);
