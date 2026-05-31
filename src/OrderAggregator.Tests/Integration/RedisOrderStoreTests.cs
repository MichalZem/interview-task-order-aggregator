using Microsoft.Extensions.Options;
using OrderAggregator.Models;
using OrderAggregator.Services.Stores;
using OrderAggregator.Shared.Configuration;
using Testcontainers.Redis;

namespace OrderAggregator.Tests.Integration;

[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class RedisOrderStoreTests : IClassFixture<RedisOrderStoreTests.RedisFixture>
{
    private readonly RedisFixture _fixture;

    public RedisOrderStoreTests(RedisFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AddAsync_AggregatesQuantitiesByProductId()
    {
        var store = CreateStore();

        await store.AddAsync(
        [
            new Order("456", 5),
            new Order("789", 42),
            new Order("456", 3),
        ]);

        var snapshot = await store.SnapshotAndClearAsync();

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(8, snapshot.Single(o => o.ProductId == "456").Quantity);
        Assert.Equal(42, snapshot.Single(o => o.ProductId == "789").Quantity);
    }

    [Fact]
    public async Task AddAsync_CommitsAllProductsInOneBatch()
    {
        // A single request carrying several distinct productIds must apply every
        // increment — exercises the MULTI/EXEC transaction queuing more than one
        // command and committing them as a whole.
        var store = CreateStore();

        await store.AddAsync(
        [
            new Order("a", 1),
            new Order("b", 2),
            new Order("c", 3),
        ]);

        var snapshot = await store.SnapshotAndClearAsync();

        Assert.Equal(3, snapshot.Count);
        Assert.Equal(1, snapshot.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(2, snapshot.Single(o => o.ProductId == "b").Quantity);
        Assert.Equal(3, snapshot.Single(o => o.ProductId == "c").Quantity);
    }

    [Fact]
    public async Task SnapshotAndClear_PreservesQuantitiesAboveIntMax()
    {
        // HINCRBY accumulates a 64-bit counter; the drain reads it as long. Verify
        // a total beyond int.MaxValue round-trips intact (the re-queue path in the
        // flush service splits such totals back into int-sized chunks).
        var store = CreateStore();
        const long overInt = (long)int.MaxValue + 100;

        await store.AddAsync([new Order("big", int.MaxValue)]);
        await store.AddAsync([new Order("big", 100)]);

        var snapshot = await store.SnapshotAndClearAsync();

        Assert.Equal(overInt, snapshot.Single(o => o.ProductId == "big").Quantity);
    }

    [Fact]
    public async Task SnapshotAndClear_ResetsState()
    {
        var store = CreateStore();
        await store.AddAsync([new Order("a", 1)]);

        await store.SnapshotAndClearAsync();
        var second = await store.SnapshotAndClearAsync();

        Assert.Empty(second);
    }

    [Fact]
    public async Task SnapshotAndClear_ReturnsEmpty_WhenNothingWritten()
    {
        var store = CreateStore();

        var snapshot = await store.SnapshotAndClearAsync();

        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task Requeue_AddsBackOntoExistingTotals()
    {
        var store = CreateStore();
        await store.AddAsync([new Order("p", 7)]);

        var drained = await store.SnapshotAndClearAsync();
        await store.AddAsync([new Order("p", 4)]);                 // new traffic after drain
        await store.AddAsync(drained.Select(a => new Order(a.ProductId, (int)a.Quantity)));

        var snapshot = await store.SnapshotAndClearAsync();
        Assert.Equal(11, snapshot.Single(o => o.ProductId == "p").Quantity);
    }

    [Fact]
    public async Task AddAsync_IsAtomic_UnderConcurrentWriters()
    {
        var store = CreateStore();
        const int Writers = 16;
        const int OrdersPerWriter = 500;
        const int ProductCount = 25;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, Writers),
            async (_, _) =>
            {
                var orders = new List<Order>(OrdersPerWriter);
                for (var i = 0; i < OrdersPerWriter; i++)
                {
                    orders.Add(new Order((i % ProductCount).ToString(), 1));
                }
                await store.AddAsync(orders);
            });

        var snapshot = await store.SnapshotAndClearAsync();

        var expectedPerProduct = (long)Writers * OrdersPerWriter / ProductCount;
        Assert.Equal(ProductCount, snapshot.Count);
        Assert.All(snapshot, agg => Assert.Equal(expectedPerProduct, agg.Quantity));
    }

    [Fact]
    public async Task DifferentInstanceIds_DoNotShareBuffer()
    {
        var baseKey = $"test:{Guid.NewGuid():N}";
        var instanceA = CreateStore(baseKey, instanceId: "instance-a");
        var instanceB = CreateStore(baseKey, instanceId: "instance-b");

        await instanceA.AddAsync([new Order("p", 10)]);
        await instanceB.AddAsync([new Order("p", 3)]);

        var snapshotA = await instanceA.SnapshotAndClearAsync();
        var snapshotB = await instanceB.SnapshotAndClearAsync();

        Assert.Equal(10, snapshotA.Single(o => o.ProductId == "p").Quantity);
        Assert.Equal(3, snapshotB.Single(o => o.ProductId == "p").Quantity);
    }

    private RedisOrderStore CreateStore(string? hashKey = null, string? instanceId = null)
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        var options = Options.Create(new OrderStoreOptions
        {
            Kind = OrderStoreKind.Redis,
            Redis = new RedisOrderStoreOptions
            {
                ConnectionString = _fixture.ConnectionString!,
                HashKey = hashKey ?? $"test:{Guid.NewGuid():N}",
                InstanceId = instanceId ?? $"inst:{Guid.NewGuid():N}",
            },
        });

        return new RedisOrderStore(_fixture.Multiplexer!, options);
    }

    public sealed class RedisFixture : IAsyncLifetime
    {
        private RedisContainer? _container;

        public StackExchange.Redis.IConnectionMultiplexer? Multiplexer { get; private set; }
        public string? ConnectionString { get; private set; }
        public string? SkipReason { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                _container = new RedisBuilder("redis:7-alpine").Build();
                await _container.StartAsync();
                ConnectionString = _container.GetConnectionString();
                Multiplexer = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(ConnectionString);
            }
            catch (Exception ex)
            {
                SkipReason = $"Redis container unavailable (Docker not reachable?): {ex.Message}";
            }
        }

        public async Task DisposeAsync()
        {
            if (Multiplexer is not null)
            {
                await Multiplexer.DisposeAsync();
            }
            if (_container is not null)
            {
                await _container.DisposeAsync();
            }
        }
    }
}
