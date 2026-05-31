using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Shapes the request body of <c>POST /api/orders</c> so the contract matches the
/// handler's real behaviour: marks the body as required, tightens the schema to a
/// non-empty array (the handler rejects a null or empty batch with 400), and
/// injects ready-to-use JSON examples for the Swagger UI / Scalar "Try it out"
/// panel instead of an empty array.
/// </summary>
public sealed class OrdersRequestOperationTransformer : IOpenApiOperationTransformer
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

        // The handler returns 400 for a null/empty body — advertise the body as required.
        if (operation.RequestBody is OpenApiRequestBody requestBody)
        {
            requestBody.Required = true;
        }

        if (operation.RequestBody.Content is null ||
            !operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            return Task.CompletedTask;
        }

        TightenArraySchema(mediaType);
        ApplyExamples(mediaType);

        return Task.CompletedTask;
    }

    // A nullable IReadOnlyCollection<OrderRequest>? handler parameter renders as
    // oneOf [ null, array ]. Unwrap to the array branch (which keeps its $ref to
    // OrderRequest) and require at least one item — the buffer never accepts an
    // empty batch.
    private static void TightenArraySchema(OpenApiMediaType mediaType)
    {
        if (mediaType.Schema is not OpenApiSchema schema || schema.OneOf is not { Count: > 0 })
        {
            return;
        }

        var arrayBranch = schema.OneOf
            .OfType<OpenApiSchema>()
            .FirstOrDefault(branch => branch.Type?.HasFlag(JsonSchemaType.Array) == true);

        if (arrayBranch is null)
        {
            return;
        }

        arrayBranch.MinItems = 1;
        mediaType.Schema = arrayBranch;
    }

    private static void ApplyExamples(OpenApiMediaType mediaType)
    {
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
    }
}
