namespace OrderAggregator.Contracts.Products;

/// <summary>
/// Wire shape of a single product. Identical to the internal domain
/// <c>Product</c> today, but lives in the contract project so the two can
/// evolve independently — e.g. adding internal audit fields to the domain
/// type won't leak into responses.
/// </summary>
/// <param name="ProductId">Unique identifier of the product.</param>
/// <param name="ProductName">Human-readable display name of the product.</param>
public sealed record ProductDto(
    string ProductId,
    string ProductName);
