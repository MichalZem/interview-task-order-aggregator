using System.Net;

namespace OrderAggregator.Tests.Integration;

// Redis-backed readiness variants. With OrderStore:Kind=Redis the readiness probe
// includes RedisHealthCheck (tagged "ready"), so its outcome reflects whether Redis
// is actually reachable — unlike the InMemory variant in HealthEndpointTests, which
// has no ready-tagged checks and is always 200.
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class HealthReadinessRedisTests : IClassFixture<RedisOrderStoreTests.RedisFixture>
{
    private readonly RedisOrderStoreTests.RedisFixture _redis;

    public HealthReadinessRedisTests(RedisOrderStoreTests.RedisFixture redis) => _redis = redis;

    [SkippableFact]
    public async Task Readiness_ReturnsOk_WhenRedisReachable()
    {
        Skip.If(_redis.SkipReason is not null, _redis.SkipReason);

        // Arrange
        using var factory = new OrderAggregatorTestFactory(_redis.ConnectionString!);
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReturnsServiceUnavailable_WhenRedisDown()
    {
        // Arrange
        // Kind=Redis pointed at a closed port: the readiness probe must report the
        // dependency as 503 Service Unavailable, not crash with 500. This needs no
        // Docker (Redis is down by definition) and guards the AbortOnConnectFail=false
        // behaviour — the multiplexer resolves without throwing so RedisHealthCheck can
        // return Unhealthy gracefully.
        using var factory = new OrderAggregatorTestFactory("localhost:1,connectTimeout=1000,syncTimeout=1000");
        using var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
