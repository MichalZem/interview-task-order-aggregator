using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Shared.Configuration;
using StackExchange.Redis;

using Order = OrderAggregator.Models.Order;
using AggregatedOrder = OrderAggregator.Models.AggregatedOrder;

namespace OrderAggregator.Services.Stores;


public sealed class RedisOrderStore : IOrderStore
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisKey _hashKey;

    public RedisOrderStore(IConnectionMultiplexer connection, IOptions<OrderStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

        var redis = options.Value.Redis;
        var instanceId = string.IsNullOrWhiteSpace(redis.InstanceId)
            ? Environment.MachineName
            : redis.InstanceId;
        _hashKey = (RedisKey)$"{redis.HashKey}:{instanceId}";
    }

    public async ValueTask AddAsync(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var db = _connection.GetDatabase();

        var transaction = db.CreateTransaction();
        var tasks = new List<Task>();
        foreach (var order in orders)
        {
            tasks.Add(transaction.HashIncrementAsync(_hashKey, order.ProductId, order.Quantity));
        }

        var committed = await transaction.ExecuteAsync().ConfigureAwait(false);
        if (!committed)
        {
            throw new RedisException("Order ingest transaction was not committed.");
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync()
    {
        var db = _connection.GetDatabase();

        var snapshotKey = (RedisKey)$"{_hashKey}:snapshot:{Guid.NewGuid():N}";

        try
        {
            await db.KeyRenameAsync(_hashKey, snapshotKey).ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("no such key", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<AggregatedOrder>();
        }

        var entries = await db.HashGetAllAsync(snapshotKey).ConfigureAwait(false);
        await db.KeyDeleteAsync(snapshotKey).ConfigureAwait(false);

        if (entries.Length == 0)
        {
            return Array.Empty<AggregatedOrder>();
        }

        var aggregated = new List<AggregatedOrder>(entries.Length);
        foreach (var entry in entries)
        {
            var productId = (string?)entry.Name;
            if (productId is null)
            {
                continue;
            }
            aggregated.Add(new AggregatedOrder(productId, (long)entry.Value));
        }

        return aggregated;
    }
}
