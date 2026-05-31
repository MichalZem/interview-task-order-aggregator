namespace OrderAggregator.Contracts.Products;

/// <summary>
/// Wrapper response so the contract has room to grow (paging metadata,
/// filters, ETag-style version) without breaking existing clients.
/// </summary>
/// <param name="Count">Number of products in <paramref name="Items"/>.</param>
/// <param name="Items">The products in the currently loaded catalog.</param>
public sealed record ProductListResponse(
    int Count,
    IReadOnlyCollection<ProductDto> Items);
