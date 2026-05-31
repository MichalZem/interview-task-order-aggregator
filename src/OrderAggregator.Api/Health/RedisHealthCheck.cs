using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace OrderAggregator.Api.Health;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisHealthCheck(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _multiplexer.GetDatabase().PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy("Redis reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis unreachable", ex);
        }
    }
}
