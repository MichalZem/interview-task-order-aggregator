using OrderAggregator.Models;

namespace OrderAggregator.Abstractions;

public interface IOrderStore
{
    ValueTask AddAsync(IEnumerable<Order> orders);

    ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync();
}
