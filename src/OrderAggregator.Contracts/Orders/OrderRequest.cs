using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Contracts.Orders;

/// <summary>
/// Wire-level representation of an incoming order. Lives in the Contracts
/// project so external consumers (SDK clients, downstream services) can
/// reference the shape without pulling in the host / domain assemblies.
/// </summary>
public sealed class OrderRequest
{
    /// <summary>
    /// Unique identifier for the product.
    /// </summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>
    /// Quantity of the product to order.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}
