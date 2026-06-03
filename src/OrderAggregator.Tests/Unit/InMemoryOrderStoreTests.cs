using OrderAggregator.Models;
using OrderAggregator.Services.Stores;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class InMemoryOrderStoreTests
{
    [Fact]
    public async Task AddAsync_AggregatesQuantitiesByProductId()
    {
        // Arrange
        var store = new InMemoryOrderStore();

        // Act
        await store.AddAsync(
        [
            new Order("456", 5),
            new Order("789", 42),
            new Order("456", 3),
        ]);

        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(8, snapshot.Single(o => o.ProductId == "456").Quantity);
        Assert.Equal(42, snapshot.Single(o => o.ProductId == "789").Quantity);
    }

    [Fact]
    public async Task DrainAsync_ResetsState()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        await store.AddAsync([new Order("a", 1)]);

        // Act
        await store.SnapshotAndClearAsync();
        var second = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Empty(second);
    }

    [Fact]
    public async Task DrainAsync_ReturnsEmpty_WhenNothingWritten()
    {
        // Arrange
        var store = new InMemoryOrderStore();

        // Act
        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task AddAsync_IsThreadSafe_UnderConcurrentWriters()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        const int Writers = 32;
        const int OrdersPerWriter = 1_000;
        const int ProductCount = 50;

        // Act
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Writers),
            async (writer, _) =>
            {
                var orders = new List<Order>(OrdersPerWriter);
                for (var i = 0; i < OrdersPerWriter; i++)
                {
                    var productId = (i % ProductCount).ToString();
                    orders.Add(new Order(productId, 1));
                }

                await store.AddAsync(orders);
            });

        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        var expectedPerProduct = (long)Writers * OrdersPerWriter / ProductCount;
        Assert.Equal(ProductCount, snapshot.Count);
        Assert.All(snapshot, agg => Assert.Equal(expectedPerProduct, agg.Quantity));
    }

    [Fact]
    public async Task DrainAsync_DoesNotLoseWrites_WhenInterleavedWithAdd()
    {
        // Arrange
        var store = new InMemoryOrderStore();
        const int TotalOrders = 10_000;
        var drained = new List<long>();
        using var cts = new CancellationTokenSource();

        // Act
        var writer = Task.Run(async () =>
        {
            for (var i = 0; i < TotalOrders; i++)
            {
                await store.AddAsync(new[] { new Order("p", 1) });
            }
        });

        var drainer = Task.Run(async () =>
        {
            while (!writer.IsCompleted)
            {
                var snapshot = await store.SnapshotAndClearAsync();
                drained.Add(snapshot.Sum(a => a.Quantity));
                await Task.Yield();
            }
        });

        await writer;
        await drainer;

        var finalSnapshot = await store.SnapshotAndClearAsync();
        var totalSeen = drained.Sum() + finalSnapshot.Sum(a => a.Quantity);

        // Assert
        Assert.Equal(TotalOrders, totalSeen);
    }
}
