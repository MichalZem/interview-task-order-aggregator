using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Shared.Configuration;

using Order = OrderAggregator.Models.Order;
using AggregatedOrder = OrderAggregator.Models.AggregatedOrder;

namespace OrderAggregator.Services.Stores;

/// <summary>
/// Write-through aggregation buffer in a single local SQLite file: every request
/// commits on its own (one fsync per request under <c>synchronous=FULL</c>). The
/// simplest durable, server-less option. For higher throughput at the same
/// durability see <see cref="SqliteGroupCommitOrderStore"/>, which coalesces
/// concurrent writes into one fsync.
/// </summary>
public sealed class SqliteOrderStore : IOrderStore, IAsyncDisposable
{
    // SQLite allows only a single writer, and Microsoft.Data.Sqlite forbids
    // overlapping commands on one connection — serialize all access ourselves.
    // Async lock (not `lock`) because the store contract is async.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SqliteConnection _connection;

    public SqliteOrderStore(IOptions<OrderStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connection = SqliteBuffer.OpenInitialized(options.Value.Sqlite.DataSource);
    }

    public async ValueTask AddAsync(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SqliteBuffer.ApplyAsync(_connection, orders).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync()
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SqliteBuffer.SnapshotAndClearAsync(_connection).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
