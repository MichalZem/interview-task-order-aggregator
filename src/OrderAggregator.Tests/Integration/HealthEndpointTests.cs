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
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_ReturnsOk_OnColdStart_Anonymously()
    {
        using var client = _factory.CreateClient();

        // The flush loop hasn't ticked within its interval yet, so the heartbeat
        // check treats it as a cold start (Healthy) rather than a stuck loop.
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
