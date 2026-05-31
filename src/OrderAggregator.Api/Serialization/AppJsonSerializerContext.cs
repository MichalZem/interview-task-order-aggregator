using System.Text.Json.Serialization;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Contracts.Products;

namespace OrderAggregator.Api.Serialization;

/// <summary>
/// System.Text.Json source-generated metadata for the wire contracts. Inserted
/// at the front of the HTTP JSON resolver chain (see <c>Program.cs</c>) so the
/// request/response (de)serialization on the hot path runs through compile-time
/// generated code instead of runtime reflection — measured against the 500-item
/// batch load test. Types not listed here fall back to the default reflection
/// resolver still present later in the chain (e.g. ProblemDetails).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyCollection<OrderRequest>))]
[JsonSerializable(typeof(OrderRequest[]))]
[JsonSerializable(typeof(OrderBatchDto))]
[JsonSerializable(typeof(AggregatedOrderDto))]
[JsonSerializable(typeof(ProductDto))]
[JsonSerializable(typeof(ProductListResponse))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;
