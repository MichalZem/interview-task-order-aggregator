namespace OrderAggregator.Contracts.Products;

/// <summary>
/// Represents a product with its unique identifier and display name.
/// </summary>
/// <param name="ProductId">Unique identifier of the product.</param>
/// <param name="ProductName">Human-readable display name of the product.</param>
public sealed record ProductDto(
    string ProductId,
    string ProductName);
