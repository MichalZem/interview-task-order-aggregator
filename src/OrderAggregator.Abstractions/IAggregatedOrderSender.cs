using OrderAggregator.Models;

namespace OrderAggregator.Abstractions;

public interface IAggregatedOrderSender
{
    Task SendAsync(OrderBatch batch);
}
