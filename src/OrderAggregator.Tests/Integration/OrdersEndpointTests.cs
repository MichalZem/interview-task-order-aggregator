using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Models;
using OrderAggregator.Services.Stores;

namespace OrderAggregator.Tests.Integration;

[Trait(TestCategories.Name, TestCategories.Integration)]
public class OrdersEndpointTests : IClassFixture<OrderAggregatorTestFactory>
{
    private readonly OrderAggregatorTestFactory _factory;

    public OrdersEndpointTests(OrderAggregatorTestFactory factory)
    {
        _factory = factory;
        _factory.Store.Reset();
    }

    [Fact]
    public async Task Post_ReturnsAccepted_AndStoresAggregatedOrders()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "456", Quantity = 5 },
            new OrderRequest { ProductId = "789", Quantity = 42 },
            new OrderRequest { ProductId = "456", Quantity = 3 },
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var snapshot = await _factory.Store.SnapshotAndClearAsync();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(8, snapshot.Single(o => o.ProductId == "456").Quantity);
        Assert.Equal(42, snapshot.Single(o => o.ProductId == "789").Quantity);
    }

    [Fact]
    public async Task Post_RejectsBatch_WhenAnyProductIdUnknown()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "456", Quantity = 5 },
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("[1].productId", body);
        Assert.Contains("Unknown productId", body);

        // Whole batch must be rejected — the valid item from index 0 must not
        // have leaked into the store.
        var snapshot = await _factory.Store.SnapshotAndClearAsync();
        Assert.Empty(snapshot);
    }

    [Fact]
    public async Task Post_ReportsEveryUnknownProductInBatch()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "missing-1", Quantity = 1 },
            new OrderRequest { ProductId = "456", Quantity = 2 },
            new OrderRequest { ProductId = "missing-2", Quantity = 3 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("[0].productId", body);
        Assert.Contains("[2].productId", body);
        Assert.DoesNotContain("[1].productId", body);
    }

    [Fact]
    public async Task Post_LocalizesError_ToCzech_WhenAcceptLanguageIsCs()
    {
        using var client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("cs");

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Deserialize rather than string-match the raw body: System.Text.Json
        // escapes diacritics (\uXXXX), so a raw Contains("Neznámé") would miss.
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        var allMessages = string.Join(" ", problem!.Errors.Values.SelectMany(v => v));
        Assert.Contains("Neznámé productId", allMessages);
        Assert.DoesNotContain("Unknown productId", allMessages);
    }

    [Fact]
    public async Task Post_UsesNeutralEnglish_WhenNoAcceptLanguageProvided()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown productId", body);
    }

    [Fact]
    public async Task Post_ReturnsUnauthorized_WhenApiKeyHeaderMissing()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("ApiKey", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Post_ReturnsUnauthorized_OnWrongApiKey()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(OrderAggregatorTestFactory.ApiKeyHeader, "not-a-valid-key");

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(OrderAggregatorTestFactory.PrimaryTestKey)]
    [InlineData(OrderAggregatorTestFactory.SecondaryTestKey)]
    public async Task Post_AcceptsBothConfiguredKeys(string key)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(OrderAggregatorTestFactory.ApiKeyHeader, key);

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Post_ReturnsValidationProblem_OnEmptyBody()
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", Array.Empty<OrderRequest>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("a", 0)]
    [InlineData("a", -3)]
    public async Task Post_ReturnsValidationProblem_OnInvalidOrder(string productId, int quantity)
    {
        using var client = _factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = productId, Quantity = quantity },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk_WithoutApiKey()
    {
        using var client = _factory.CreateClient();

        // /health/live is the anonymous liveness probe (the bare /health alias was
        // dropped as a duplicate of /health/live + /health/ready).
        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsAccessibleAnonymously_AndDeclaresSecurityScheme()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ApiKey\"", body);
        Assert.Contains("\"X-Api-Key\"", body);
    }

    [Fact]
    public async Task OpenApiDocument_ExposesCultureQueryParameter_AsLanguageDropdown()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The culture parameter + its enum values let Swagger UI / Scalar render
        // a language dropdown for trying out localized responses. Parse rather
        // than string-match so whitespace in serialization can't break the test.
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        var cultureParam = doc.RootElement
            .GetProperty("paths").GetProperty("/api/orders").GetProperty("post")
            .GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "culture");

        Assert.Equal("query", cultureParam.GetProperty("in").GetString());
        var enumValues = cultureParam.GetProperty("schema").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("en", enumValues);
        Assert.Contains("cs", enumValues);
    }
}

public sealed class OrderAggregatorTestFactory : WebApplicationFactory<Program>
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string PrimaryTestKey = "test-key-primary-1234567890";
    public const string SecondaryTestKey = "test-key-secondary-1234567890";

    public TestOrderStore Store { get; } = new();
    public TestProductRepository Products { get; } = new(new[] { "1", "456", "789" });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeader, PrimaryTestKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKey:HeaderName"] = ApiKeyHeader,
                ["ApiKey:Keys:0:Name"] = "primary-test",
                ["ApiKey:Keys:0:Key"] = PrimaryTestKey,
                ["ApiKey:Keys:1:Name"] = "secondary-test",
                ["ApiKey:Keys:1:Key"] = SecondaryTestKey,
                // No OTLP collector under test — keep the exporter off so the
                // test host doesn't spin up background export connections.
                ["Observability:Enabled"] = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderStore>();
            services.AddSingleton<IOrderStore>(Store);

            services.RemoveAll<IAggregatedOrderSender>();
            services.AddSingleton<IAggregatedOrderSender, NullSender>();

            // Replace the file-backed catalog so tests don't depend on the
            // products.json shipped with the API project.
            services.RemoveAll<IProductRepository>();
            services.AddSingleton<IProductRepository>(Products);
        });
    }
}

public sealed class TestOrderStore : IOrderStore
{
    private readonly InMemoryOrderStore _inner = new();

    public void Reset() => _ = _inner.SnapshotAndClearAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask AddAsync(IEnumerable<Order> orders)
        => _inner.AddAsync(orders);

    public ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync()
        => _inner.SnapshotAndClearAsync();
}

public sealed class TestProductRepository : IProductRepository
{
    private readonly Dictionary<string, Product> _byId;

    public TestProductRepository(IEnumerable<string> productIds)
    {
        _byId = productIds.ToDictionary(
            id => id,
            id => new Product(id, $"Test product {id}"),
            StringComparer.Ordinal);
    }

    public int Count => _byId.Count;
    public bool Exists(string productId) => _byId.ContainsKey(productId);
    public Product? Find(string productId) => _byId.GetValueOrDefault(productId);
    public IReadOnlyCollection<Product> GetAll() => _byId.Values;
}

public sealed class NullSender : IAggregatedOrderSender
{
    public Task SendAsync(OrderBatch batch) => Task.CompletedTask;
}
