using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Contracts.Orders;

/// <summary>
/// Represents a request to create an order for a product with a specified quantity.
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
