using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Models;
using OrderAggregator.Services.Stores;
// Aliased rather than `using StackExchange.Redis` to avoid clashing with
// OrderAggregator.Models.Order (StackExchange.Redis also defines an Order type).
using ConfigurationOptions = StackExchange.Redis.ConfigurationOptions;
using ConnectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer;
using IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer;

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
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "456", Quantity = 5 },
            new OrderRequest { ProductId = "789", Quantity = 42 },
            new OrderRequest { ProductId = "456", Quantity = 3 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var snapshot = await _factory.Store.SnapshotAndClearAsync();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(8, snapshot.Single(o => o.ProductId == "456").Quantity);
        Assert.Equal(42, snapshot.Single(o => o.ProductId == "789").Quantity);
    }

    [Fact]
    public async Task Post_RejectsBatch_WhenAnyProductIdUnknown()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "456", Quantity = 5 },
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        // Assert
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
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "missing-1", Quantity = 1 },
            new OrderRequest { ProductId = "456", Quantity = 2 },
            new OrderRequest { ProductId = "missing-2", Quantity = 3 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("[0].productId", body);
        Assert.Contains("[2].productId", body);
        Assert.DoesNotContain("[1].productId", body);
    }

    [Fact]
    public async Task Post_LocalizesError_ToCzech_WhenAcceptLanguageIsCs()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("cs");

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        // Assert
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
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "does-not-exist", Quantity = 1 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown productId", body);
    }

    [Fact]
    public async Task Post_ReturnsUnauthorized_WhenApiKeyHeaderMissing()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("ApiKey", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Post_ReturnsUnauthorized_OnWrongApiKey()
    {
        // Arrange
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(OrderAggregatorTestFactory.ApiKeyHeader, "not-a-valid-key");

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(OrderAggregatorTestFactory.PrimaryTestKey)]
    [InlineData(OrderAggregatorTestFactory.SecondaryTestKey)]
    public async Task Post_AcceptsBothConfiguredKeys(string key)
    {
        // Arrange
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(OrderAggregatorTestFactory.ApiKeyHeader, key);

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = "1", Quantity = 1 },
        });

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Post_ReturnsValidationProblem_OnEmptyBody()
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", Array.Empty<OrderRequest>());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("a", 0)]
    [InlineData("a", -3)]
    public async Task Post_ReturnsValidationProblem_OnInvalidOrder(string productId, int quantity)
    {
        // Arrange
        using var client = _factory.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", new[]
        {
            new OrderRequest { ProductId = productId, Quantity = quantity },
        });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsOk_WithoutApiKey()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        // /health/live is the anonymous liveness probe (the bare /health alias was
        // dropped as a duplicate of /health/live + /health/ready).
        var response = await client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsAccessibleAnonymously_AndDeclaresSecurityScheme()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"ApiKey\"", body);
        Assert.Contains("\"X-Api-Key\"", body);
    }

    [Fact]
    public async Task OpenApiDocument_ExposesCultureQueryParameter_AsLanguageDropdown()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
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

    private readonly string? _redisConnectionString;

    public TestOrderStore Store { get; } = new();
    public TestProductRepository Products { get; } = new(new[] { "1", "456", "789" });

    // Default: behave like the documented InMemory backend. appsettings.json ships
    // OrderStore:Kind=Redis and the store-kind switch is read eagerly at registration
    // (before the test's config overrides apply), so the host always wires up the
    // Redis health check + multiplexer. We strip them here so readiness aggregates an
    // empty set — matching what InMemory mode actually produces.
    public OrderAggregatorTestFactory()
    {
    }

    // Internal (not public): xUnit class fixtures must expose a single public ctor.
    // The Redis readiness variants construct the factory directly via this overload,
    // which keeps the Redis health check and points its multiplexer at the given
    // endpoint (a live container for the healthy case, a dead port for the down case).
    internal OrderAggregatorTestFactory(string redisConnectionString)
    {
        _redisConnectionString = redisConnectionString;
    }

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

            ConfigureRedisReadiness(services);
        });
    }

    // Adjust the Redis wiring the host always registers (appsettings ships
    // OrderStore:Kind=Redis). Done here in ConfigureServices, not via configuration,
    // because the store-kind switch reads config eagerly at registration — before the
    // test's ConfigureAppConfiguration overrides take effect.
    private void ConfigureRedisReadiness(IServiceCollection services)
    {
        if (_redisConnectionString is null)
        {
            // InMemory default: drop the Redis health check so readiness has no
            // "ready"-tagged checks and the multiplexer never tries to connect.
            services.RemoveAll<IConnectionMultiplexer>();
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                var redis = options.Registrations.FirstOrDefault(r => r.Name == "redis");
                if (redis is not null)
                {
                    options.Registrations.Remove(redis);
                }
            });
            return;
        }

        // Redis variants: keep the health check but point the multiplexer at the
        // chosen endpoint. AbortOnConnectFail=false mirrors production so a dead
        // endpoint yields Unhealthy (503) instead of throwing at resolution (500).
        services.RemoveAll<IConnectionMultiplexer>();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var config = ConfigurationOptions.Parse(_redisConnectionString);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
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
