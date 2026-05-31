using System.Net;
using System.Text.Json;

namespace OrderAggregator.Tests.Integration;

[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class OpenApiDocumentTests : IClassFixture<OrderAggregatorTestFactory>
{
    private readonly OrderAggregatorTestFactory _factory;

    public OpenApiDocumentTests(OrderAggregatorTestFactory factory)
    {
        _factory = factory;
    }

    private async Task<JsonDocument> GetOpenApiDocumentAsync()
    {
        // The OpenAPI document is anonymous — no API key needed.
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Document_Tags_HaveDescriptions()
    {
        using var document = await GetOpenApiDocumentAsync();

        var tags = document.RootElement.GetProperty("tags");
        foreach (var name in new[] { "Orders", "Products" })
        {
            var tag = tags.EnumerateArray()
                .Single(t => t.GetProperty("name").GetString() == name);

            Assert.False(string.IsNullOrWhiteSpace(tag.GetProperty("description").GetString()));
        }
    }

    [Fact]
    public async Task PostOrders_RequestBody_IsRequiredNonEmptyArray()
    {
        using var document = await GetOpenApiDocumentAsync();

        var post = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/orders")
            .GetProperty("post");

        var requestBody = post.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());

        var schema = requestBody
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        // The nullable oneOf wrapper is unwrapped to a plain non-empty array.
        Assert.False(schema.TryGetProperty("oneOf", out _));
        Assert.Equal("array", schema.GetProperty("type").GetString());
        Assert.Equal(1, schema.GetProperty("minItems").GetInt32());
    }

    [Theory]
    [InlineData("OrderRequest", "quantity")]
    [InlineData("ProductListResponse", "count")]
    public async Task IntegerProperties_AreCleanIntegers_NotStringUnions(string schemaName, string propertyName)
    {
        using var document = await GetOpenApiDocumentAsync();

        var property = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty(propertyName);

        // Strict number handling drops AllowReadingFromString, so the schema is a
        // plain integer rather than an integer|string union with a numeric pattern.
        Assert.Equal("integer", property.GetProperty("type").GetString());
        Assert.False(property.TryGetProperty("pattern", out _));
    }

    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("HttpValidationProblemDetails")]
    public async Task ProblemDetailSchemas_HaveDescriptions(string schemaName)
    {
        using var document = await GetOpenApiDocumentAsync();

        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);

        Assert.True(schema.TryGetProperty("description", out var description));
        Assert.False(string.IsNullOrWhiteSpace(description.GetString()));
    }

    [Fact]
    public async Task PostOrders_DeclaresServerErrorResponse()
    {
        using var document = await GetOpenApiDocumentAsync();

        var responses = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/orders")
            .GetProperty("post")
            .GetProperty("responses");

        Assert.True(responses.TryGetProperty("500", out _));
    }
}
