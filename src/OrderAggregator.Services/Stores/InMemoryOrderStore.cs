using OrderAggregator.Abstractions;
using OrderAggregator.Models;

namespace OrderAggregator.Services.Stores;

public sealed class InMemoryOrderStore : IOrderStore
{
    private readonly Lock _lock = new();

    private Dictionary<string, long> _aggregated = new(StringComparer.Ordinal);

    public ValueTask AddAsync(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        lock (_lock)
        {
            foreach (var order in orders)
            {
                _aggregated.TryGetValue(order.ProductId, out var existing);
                _aggregated[order.ProductId] = existing + order.Quantity;
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync()
    {
        Dictionary<string, long> snapshot;

        lock (_lock)
        {
            snapshot = _aggregated;
            _aggregated = new Dictionary<string, long>(StringComparer.Ordinal);
        }

        if (snapshot.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyCollection<AggregatedOrder>>(Array.Empty<AggregatedOrder>());
        }

        var result = new List<AggregatedOrder>(snapshot.Count);
        foreach (var (productId, quantity) in snapshot)
        {
            result.Add(new AggregatedOrder(productId, quantity));
        }

        return ValueTask.FromResult<IReadOnlyCollection<AggregatedOrder>>(result);
    }
}
