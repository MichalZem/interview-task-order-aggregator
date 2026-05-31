using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Configuration for the JSON-file backed product catalog. Bound from the
/// <c>ProductCatalog</c> section of configuration.
/// </summary>
public sealed class ProductCatalogOptions
{
    public const string SectionName = "ProductCatalog";

    /// <summary>
    /// Path to the JSON file with the product list. Relative paths are
    /// resolved against the content root (typical layout: file is shipped
    /// inside the API project and copied to the output directory).
    /// </summary>
    [Required]
    [StringLength(512, MinimumLength = 1)]
    public string FilePath { get; set; } = "products.json";
}
