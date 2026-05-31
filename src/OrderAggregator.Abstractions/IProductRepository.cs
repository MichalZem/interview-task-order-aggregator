using OrderAggregator.Models;

namespace OrderAggregator.Abstractions;

public interface IProductRepository
{
    int Count { get; }

    bool Exists(string productId);

    Product? Find(string productId);

    IReadOnlyCollection<Product> GetAll();
}
