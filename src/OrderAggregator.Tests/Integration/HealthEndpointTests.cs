using System.Net;

namespace OrderAggregator.Tests.Integration;

[Trait(TestCategories.Name, TestCategories.Integration)]
public class HealthEndpointTests : IClassFixture<OrderAggregatorTestFactory>
{
    private readonly OrderAggregatorTestFactory _factory;

    public HealthEndpointTests(OrderAggregatorTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Liveness_ReturnsOk_Anonymously()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReturnsOk_OnColdStart_Anonymously()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        // InMemory store registers no "ready"-tagged checks, so readiness aggregates
        // an empty set and reports 200 — this asserts the endpoint wiring + anonymous
        // access. The Redis variants (live + down) live in HealthReadinessRedisTests.
        var response = await client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
