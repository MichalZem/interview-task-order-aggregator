using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;

namespace OrderAggregator.Services.Products;

public sealed class JsonFileProductRepository : IProductRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FrozenDictionary<string, Product> _byId;

    private JsonFileProductRepository(FrozenDictionary<string, Product> byId)
    {
        _byId = byId;
    }

    public int Count => _byId.Count;

    public bool Exists(string productId) =>
        !string.IsNullOrEmpty(productId) && _byId.ContainsKey(productId);

    public Product? Find(string productId) =>
        !string.IsNullOrEmpty(productId) && _byId.TryGetValue(productId, out var product)
            ? product
            : null;

    public IReadOnlyCollection<Product> GetAll() => _byId.Values;

    public static JsonFileProductRepository LoadFromFile(string path, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Product catalog file not found at '{path}'. Configure ProductCatalog:FilePath or ship the file with the application.",
                path);
        }

        List<Product>? products;
        using (var stream = File.OpenRead(path))
        {
            products = JsonSerializer.Deserialize<List<Product>>(stream, JsonOptions);
        }

        if (products is null || products.Count == 0)
        {
            throw new InvalidOperationException(
                $"Product catalog at '{path}' is empty or could not be parsed; refusing to start with no products.");
        }

        var builder = new Dictionary<string, Product>(products.Count, StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var product in products)
        {
            if (string.IsNullOrWhiteSpace(product.ProductId) || string.IsNullOrWhiteSpace(product.ProductName))
            {
                logger.LogWarning("Skipping product entry with missing ProductId or ProductName: {Product}", product);
                continue;
            }

            if (!builder.TryAdd(product.ProductId, product))
            {
                duplicates++;
                logger.LogWarning(
                    "Duplicate productId '{ProductId}' in catalog '{Path}'; keeping the first occurrence",
                    product.ProductId,
                    path);
            }
        }

        if (builder.Count == 0)
        {
            throw new InvalidOperationException(
                $"Product catalog at '{path}' contained {products.Count} entries but none were valid.");
        }

        logger.LogInformation(
            "Loaded {Count} products from '{Path}' (skipped {Duplicates} duplicate entries)",
            builder.Count,
            path,
            duplicates);

        return new JsonFileProductRepository(builder.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
