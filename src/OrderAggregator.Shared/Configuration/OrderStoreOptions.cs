using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Selects which <c>IOrderStore</c> implementation backs the aggregation buffer.
/// Bound from configuration section <c>OrderStore</c>. Mirrors the pluggable
/// sender selection — persistence is meant to be swappable per environment
/// (in-memory for dev/tests, Redis for a multi-instance production deployment).
/// </summary>
public sealed class OrderStoreOptions
{
    public const string SectionName = "OrderStore";

    [Required]
    public OrderStoreKind Kind { get; set; } = OrderStoreKind.InMemory;

    /// <summary>Redis-specific settings, used only when <see cref="Kind"/> is <c>Redis</c>.</summary>
    public RedisOrderStoreOptions Redis { get; set; } = new();
}

public enum OrderStoreKind
{
    /// <summary>Process-local buffer. Default; fast, but lost on restart and not shared across instances.</summary>
    InMemory,

    /// <summary>Redis-backed buffer (HINCRBY hash). Survives restarts and is shared across instances.</summary>
    Redis,
}

/// <summary>
/// Configuration for <c>RedisOrderStore</c>. Each instance owns a private hash
/// keyed <c>{HashKey}:{InstanceId}</c> (productId → quantity); writers use atomic
/// HINCRBY and the drain atomically RENAMEs that hash aside (then reads + deletes
/// the retired key), so concurrent writers never lose increments between read and
/// clear. Per-instance keys keep each instance's snapshot separate and
/// traceable — an instance only ever drains its own data, never another's.
/// </summary>
public sealed class RedisOrderStoreOptions
{
    /// <summary>
    /// StackExchange.Redis connection string (e.g. <c>localhost:6379</c> or a full
    /// configuration with password / SSL). Required when the store kind is Redis.
    /// </summary>
    [Required]
    [StringLength(2048, MinimumLength = 1)]
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Base Redis key prefix. The store appends <see cref="InstanceId"/> to form
    /// the actual per-instance hash key, so multiple instances sharing one Redis
    /// never write into the same buffer.
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string HashKey { get; set; } = "order-aggregator:pending";

    /// <summary>
    /// Identifies this instance within the shared Redis. Appended to
    /// <see cref="HashKey"/> so each instance buffers and drains its own data.
    /// When left empty, the store falls back to the machine/host name
    /// (the container ID under Docker) — stable across process restarts on the
    /// same host, so a restart re-attaches to any buffer left behind. Set
    /// explicitly only when the host name isn't unique or stable enough.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;
}
