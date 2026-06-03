using System.Net;
using System.Net.Http.Json;
using OrderAggregator.Contracts.Products;

namespace OrderAggregator.Tests.Integration;

[Trait(TestCategories.Name, TestCategories.Integration)]
public class ProductsEndpointTests : IClassFixture<OrderAggregatorTestFactory>
{
    private readonly OrderAggregatorTestFactory _factory;

    public ProductsEndpointTests(OrderAggregatorTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_ReturnsAllConfiguredProducts()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProductListResponse>();
        Assert.NotNull(payload);
        Assert.Equal(_factory.Products.Count, payload!.Count);
        Assert.Equal(payload.Count, payload.Items.Count);
        Assert.All(payload.Items, p =>
        {
            Assert.False(string.IsNullOrEmpty(p.ProductId));
            Assert.False(string.IsNullOrEmpty(p.ProductName));
        });
    }

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutApiKey()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/products");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsProduct_WhenIdExists()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/products/456");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);
        Assert.Equal("456", product!.ProductId);
        Assert.False(string.IsNullOrEmpty(product.ProductName));
    }

    [Fact]
    public async Task Get_Returns404_WhenIdMissing()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/products/does-not-exist");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsUnauthorized_WithoutApiKey()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/products/456");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
