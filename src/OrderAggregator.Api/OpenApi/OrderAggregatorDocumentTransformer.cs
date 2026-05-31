using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Fills in the <c>info</c> section of the generated OpenAPI document so the
/// rendered Scalar / Swagger UI pages show a meaningful title, version and
/// description instead of the project name fallback.
/// </summary>
public sealed class OrderAggregatorDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Order Aggregator API",
            Version = "v1",
            Description =
                "Accepts orders and, within a single 20-second window, aggregates them by productId. " +
                "The aggregated snapshot is periodically handed off to an internal system " +
                "(in this demo via ConsoleAggregatedOrderSender).",
            Contact = new OpenApiContact
            {
                Name = "Michal Zemek - https://github.com/MichalZem/interview-task-order-aggregator - GitHub",
                Url = new Uri("https://github.com/MichalZem/interview-task-order-aggregator"),
                
            },
            License = new OpenApiLicense
            {
                Name = "MIT",
            },
        };

        // Tag descriptions render as section subtitles in Scalar / Swagger UI.
        // Names must match the .WithTags(...) values on the endpoint groups so the
        // per-operation tag references resolve to these definitions.
        document.Tags = new HashSet<OpenApiTag>
        {
            new()
            {
                Name = "Orders",
                Description = "Submit orders for aggregation into the rolling 20-second flush window.",
            },
            new()
            {
                Name = "Products",
                Description = "Read the product catalog that incoming orders are validated against.",
            },
        };

        return Task.CompletedTask;
    }
}
