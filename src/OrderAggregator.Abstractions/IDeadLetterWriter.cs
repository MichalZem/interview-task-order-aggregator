using OrderAggregator.Models;

namespace OrderAggregator.Abstractions;

public interface IDeadLetterWriter
{
    Task WriteAsync(OrderBatch batch);
}
