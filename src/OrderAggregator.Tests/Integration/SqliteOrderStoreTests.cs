using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;
using OrderAggregator.Services.Stores;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Tests.Integration;

/// <summary>
/// Shared contract tests for the SQLite-backed stores. Both the write-through
/// (<see cref="SqliteOrderStore"/>) and group-commit
/// (<see cref="SqliteGroupCommitOrderStore"/>) variants must behave identically;
/// each concrete subclass just supplies its factory. No Docker — a temp file.
/// </summary>
public abstract class SqliteOrderStoreTestsBase<TStore> : IDisposable
    where TStore : class, IOrderStore, IAsyncDisposable
{
    // Unique file per test instance (xUnit creates one instance per test method),
    // so tests never share a buffer.
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"order-aggregator-test-{Guid.NewGuid():N}.db");

    protected abstract TStore CreateStore(IOptions<OrderStoreOptions> options);

    [Fact]
    public async Task AddAsync_AggregatesQuantitiesByProductId()
    {
        // Arrange
        await using var store = NewStore();

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
    public async Task AddAsync_CommitsAllProductsInOneBatch()
    {
        // Arrange
        await using var store = NewStore();

        // Act
        await store.AddAsync(
        [
            new Order("a", 1),
            new Order("b", 2),
            new Order("c", 3),
        ]);

        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(1, snapshot.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(2, snapshot.Single(o => o.ProductId == "b").Quantity);
        Assert.Equal(3, snapshot.Single(o => o.ProductId == "c").Quantity);
    }

    [Fact]
    public async Task SnapshotAndClear_PreservesQuantitiesAboveIntMax()
    {
        // Arrange
        // The buffer column is a 64-bit INTEGER; the drain reads it as long. Verify
        // a total beyond int.MaxValue round-trips intact.
        await using var store = NewStore();
        const long OverInt = (long)int.MaxValue + 100;

        // Act
        await store.AddAsync([new Order("big", int.MaxValue)]);
        await store.AddAsync([new Order("big", 100)]);

        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Equal(OverInt, snapshot.Single(o => o.ProductId == "big").Quantity);
    }

    [Fact]
    public async Task SnapshotAndClear_ResetsState()
    {
        // Arrange
        await using var store = NewStore();
        await store.AddAsync([new Order("a", 1)]);

        // Act
        await store.SnapshotAndClearAsync();
        var second = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Empty(second);
    }

    [Fact]
    public async Task SnapshotAndClear_ReturnsEmpty_WhenNothingWritten()
    {
        // Arrange
        await using var store = NewStore();

        // Act
        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task Requeue_AddsBackOntoExistingTotals()
    {
        // Arrange
        await using var store = NewStore();
        await store.AddAsync([new Order("p", 7)]);

        // Act
        var drained = await store.SnapshotAndClearAsync();
        await store.AddAsync([new Order("p", 4)]);                 // new traffic after drain
        await store.AddAsync(drained.Select(a => new Order(a.ProductId, (int)a.Quantity)));

        var snapshot = await store.SnapshotAndClearAsync();

        // Assert
        Assert.Equal(11, snapshot.Single(o => o.ProductId == "p").Quantity);
    }

    [Fact]
    public async Task AddAsync_IsAtomic_UnderConcurrentWriters()
    {
        // Arrange
        // The key test for group commit: many concurrent AddAsync calls coalesce
        // into shared transactions, yet every single increment must land.
        await using var store = NewStore();
        const int Writers = 16;
        const int OrdersPerWriter = 500;
        const int ProductCount = 25;

        // Act
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

        // Assert
        var expectedPerProduct = (long)Writers * OrdersPerWriter / ProductCount;
        Assert.Equal(ProductCount, snapshot.Count);
        Assert.All(snapshot, agg => Assert.Equal(expectedPerProduct, agg.Quantity));
    }

    [Fact]
    public async Task Buffer_SurvivesReopen_OnSameFile()
    {
        // Arrange
        // The whole point of a SQLite store: a buffer left behind by a stopped
        // process is picked up by the next one. Write, fully close, reopen on the
        // same file, and the pending total must still be there to drain.
        await using (var first = NewStore())
        {
            await first.AddAsync([new Order("p", 5)]);
            await first.AddAsync([new Order("p", 3)]);
        }

        // Act
        await using var second = NewStore();
        var snapshot = await second.SnapshotAndClearAsync();

        // Assert
        Assert.Equal(8, snapshot.Single(o => o.ProductId == "p").Quantity);
    }

    private TStore NewStore()
    {
        var options = Options.Create(new OrderStoreOptions
        {
            Sqlite = new SqliteOrderStoreOptions { DataSource = _dbPath },
        });

        return CreateStore(options);
    }

    public void Dispose()
    {
        // Remove the temp database and its WAL sidecar files.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_dbPath + suffix);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp dir is reclaimed eventually.
            }
        }
    }
}

[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SqliteOrderStoreTests : SqliteOrderStoreTestsBase<SqliteOrderStore>
{
    protected override SqliteOrderStore CreateStore(IOptions<OrderStoreOptions> options) => new(options);
}

[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SqliteGroupCommitOrderStoreTests : SqliteOrderStoreTestsBase<SqliteGroupCommitOrderStore>
{
    protected override SqliteGroupCommitOrderStore CreateStore(IOptions<OrderStoreOptions> options) => new(options);
}
