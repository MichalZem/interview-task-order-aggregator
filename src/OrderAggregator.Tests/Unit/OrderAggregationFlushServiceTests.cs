using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Shared.Configuration;
using OrderAggregator.Models;
using OrderAggregator.Services.OrderAggregation;
using OrderAggregator.Services.Stores;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class OrderAggregationFlushServiceTests
{
    [Fact]
    public async Task FlushOnce_SendsAggregatedBatch_AndDrainsStore()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        var sender = new CapturingSender();
        var service = CreateService(store, sender);
        await store.AddAsync(new[] { new Order("a", 2), new Order("b", 1), new Order("a", 3) });

        // Act
        await service.FlushOnceAsync();

        // Assert
        var batch = Assert.Single(sender.Batches);
        Assert.Equal(2, batch.Orders.Count);
        Assert.Equal(5, batch.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(1, batch.Orders.Single(o => o.ProductId == "b").Quantity);

        var afterDrain = await store.SnapshotAndClearAsync();
        Assert.Empty(afterDrain);
    }

    [Fact]
    public async Task FlushOnce_SkipsSend_WhenStoreIsEmpty()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        var sender = new CapturingSender();
        var service = CreateService(store, sender);

        // Act
        await service.FlushOnceAsync();

        // Assert
        Assert.Empty(sender.Batches);
    }

    [Fact]
    public async Task FlushOnce_DeadLettersBatch_AndDoesNotRequeue_WhenSenderAlwaysFails()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        var sender = new FlakySender(failFirstAttempts: int.MaxValue); // never recovers
        var deadLetter = new CapturingDeadLetterSink();
        var service = CreateService(store, sender, deadLetter);
        await store.AddAsync(new[] { new Order("a", 7), new Order("b", 4) });

        // Act
        await service.FlushOnceAsync(); // exhausts retries, dead-letters

        // Assert
        Assert.Empty(sender.SuccessfulBatches);

        // Failed batch went to the dead-letter sink with the aggregated totals.
        var dead = Assert.Single(deadLetter.Batches);
        Assert.Equal(7, dead.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(4, dead.Orders.Single(o => o.ProductId == "b").Quantity);

        // Nothing was re-queued: the next flush has nothing left to send.
        await service.FlushOnceAsync();
        Assert.Empty(sender.SuccessfulBatches);
        Assert.Single(deadLetter.Batches);
    }

    [Fact]
    public async Task FlushOnce_RetriesAndSucceeds_WithoutDeadLettering()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        var sender = new FlakySender(failFirstAttempts: 1); // recovers on the retry
        var deadLetter = new CapturingDeadLetterSink();
        var service = CreateService(store, sender, deadLetter);
        await store.AddAsync(new[] { new Order("a", 7) });

        // Act
        await service.FlushOnceAsync();

        // Assert
        var sent = Assert.Single(sender.SuccessfulBatches);
        Assert.Equal(7, sent.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Empty(deadLetter.Batches);
    }

    private static OrderAggregationFlushService CreateService(
        IOrderStore store,
        IAggregatedOrderSender sender,
        IDeadLetterWriter? deadLetter = null,
        TimeProvider? timeProvider = null,
        int intervalSeconds = 20)
    {
        var options = Options.Create(new AggregationOptions
        {
            FlushIntervalSeconds = intervalSeconds,
            SendRetryDelayMilliseconds = 0, // keep retries instant in tests
        });
        return new OrderAggregationFlushService(
            store,
            sender,
            deadLetter ?? new CapturingDeadLetterSink(),
            options,
            NullLogger<OrderAggregationFlushService>.Instance,
            timeProvider ?? TimeProvider.System);
    }

    private sealed class CapturingDeadLetterSink : IDeadLetterWriter
    {
        private readonly List<OrderBatch> _batches = new();
        public IReadOnlyList<OrderBatch> Batches
        {
            get
            {
                lock (_batches)
                {
                    return _batches.ToList();
                }
            }
        }

        public Task WriteAsync(OrderBatch batch)
        {
            lock (_batches)
            {
                _batches.Add(batch);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSender : IAggregatedOrderSender
    {
        private readonly List<OrderBatch> _batches = new();
        public IReadOnlyList<OrderBatch> Batches
        {
            get
            {
                lock (_batches)
                {
                    return _batches.ToList();
                }
            }
        }

        public Task SendAsync(OrderBatch batch)
        {
            lock (_batches)
            {
                _batches.Add(batch);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FlakySender : IAggregatedOrderSender
    {
        private readonly int _failFirstAttempts;
        private int _attempts;
        private readonly List<OrderBatch> _successful = new();

        public FlakySender(int failFirstAttempts)
        {
            _failFirstAttempts = failFirstAttempts;
        }

        public IReadOnlyList<OrderBatch> SuccessfulBatches
        {
            get
            {
                lock (_successful)
                {
                    return _successful.ToList();
                }
            }
        }

        public Task SendAsync(OrderBatch batch)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt <= _failFirstAttempts)
            {
                throw new InvalidOperationException("Simulated send failure");
            }

            lock (_successful)
            {
                _successful.Add(batch);
            }
            return Task.CompletedTask;
        }
    }
}
