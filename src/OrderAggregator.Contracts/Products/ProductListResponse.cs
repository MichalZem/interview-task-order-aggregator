namespace OrderAggregator.Contracts.Products;

/// <summary>
/// Represents the response containing a list of products in the currently loaded catalog.
/// </summary>
/// <param name="Count">Number of products in <paramref name="Items"/>.</param>
/// <param name="Items">The products in the currently loaded catalog.</param>
public sealed record ProductListResponse(
    int Count,
    IReadOnlyCollection<ProductDto> Items);
