using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace OrderAggregator.Api.OpenApi;

/// <summary>
/// Adds human-readable descriptions to the two framework problem-detail schemas
/// (RFC 9457). They originate in Microsoft.AspNetCore.* — not our XML-documented
/// wire DTOs in Contracts — so the generator emits them into
/// <c>components.schemas</c> without any description. This makes the error contract
/// self-explanatory in Scalar / Swagger UI.
/// </summary>
public sealed class ProblemDetailsSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(HttpValidationProblemDetails))
        {
            schema.Description =
                "RFC 9457 problem details for a failed request validation (HTTP 400). Extends the " +
                "standard problem object with an `errors` map keyed by the offending field " +
                "(e.g. `[0].productId`), each mapping to one or more human-readable messages.";
        }
        else if (type == typeof(ProblemDetails))
        {
            schema.Description =
                "RFC 9457 problem details describing an error response (e.g. 401, 404, 500). A " +
                "machine-readable error object with `type`, `title`, `status`, `detail` and `instance`.";
        }

        return Task.CompletedTask;
    }
}
