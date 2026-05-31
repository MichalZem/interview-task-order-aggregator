using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Injects ready-to-use JSON examples into the request body of
/// <c>POST /api/orders</c> so the Swagger UI / Scalar "Try it out" panel
/// prefills with a realistic payload instead of an empty array.
/// </summary>
public sealed class OrdersExamplesOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly JsonArray TypicalBatch = new()
    {
        new JsonObject { ["productId"] = "456", ["quantity"] = 5 },
        new JsonObject { ["productId"] = "789", ["quantity"] = 42 },
        new JsonObject { ["productId"] = "456", ["quantity"] = 3 },
    };

    private static readonly JsonArray SingleOrder = new()
    {
        new JsonObject { ["productId"] = "456", ["quantity"] = 1 },
    };

    private static readonly JsonArray HighVolumeBurst = new()
    {
        new JsonObject { ["productId"] = "p-001", ["quantity"] = 250 },
        new JsonObject { ["productId"] = "p-002", ["quantity"] = 175 },
        new JsonObject { ["productId"] = "p-003", ["quantity"] = 90 },
        new JsonObject { ["productId"] = "p-001", ["quantity"] = 60 },
    };

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.OperationId != "AcceptOrders" || operation.RequestBody is null)
        {
            return Task.CompletedTask;
        }

        if (operation.RequestBody.Content is null ||
            !operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            return Task.CompletedTask;
        }

        // Single inline example — Swagger UI shows this as the prefilled body.
        mediaType.Example = TypicalBatch.DeepClone();

        // Named examples — Swagger UI renders them in a dropdown, Scalar in tabs.
        mediaType.Examples = new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal)
        {
            ["typical-batch"] = new OpenApiExample
            {
                Summary = "Typical batch (3 items, duplicate productId)",
                Description = "Two orders for product 456 sum to quantity=8.",
                Value = TypicalBatch.DeepClone(),
            },
            ["single-order"] = new OpenApiExample
            {
                Summary = "Single order",
                Description = "The smallest possible valid input.",
                Value = SingleOrder.DeepClone(),
            },
            ["burst"] = new OpenApiExample
            {
                Summary = "High-volume burst",
                Description = "4 items across 3 products — shows aggregation across multiple keys.",
                Value = HighVolumeBurst.DeepClone(),
            },
        };

        return Task.CompletedTask;
    }
}
