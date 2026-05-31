using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Models;
using OrderAggregator.Resources;
using OrderAggregator.Services.Diagnostics;

namespace OrderAggregator.Api.Endpoints;

public static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders")
            .WithTags("Orders")
            .RequireAuthorization();

        group.MapPost("/", AcceptOrdersAsync)
            .WithName("AcceptOrders")
            .WithSummary("Accepts one or more orders for aggregation")
            .WithDescription(
                "Input is a JSON array of `{ productId, quantity }`. Items are aggregated into an " +
                "internal buffer and, in the next flush window (every 20 s by default), are sent to " +
                "the downstream system. The endpoint is fire-and-forget — it returns 202 Accepted " +
                "after validation and buffering, not after the aggregated batch is delivered. Orders " +
                "referencing an unknown productId are rejected (400) — the whole batch fails, even " +
                "the valid items.")
            .Accepts<IReadOnlyCollection<OrderRequest>>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<Results<Accepted, ValidationProblem>> AcceptOrdersAsync(
        [FromBody] IReadOnlyCollection<OrderRequest>? requests,
        IOrderStore store,
        IProductRepository products,
        OrderAggregationMetrics metrics)
    {
        if (requests is null || requests.Count == 0)
        {
            metrics.RecordBatchRejected();
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = [ApiMessages.OrderBatchEmpty],
            });
        }

        // Validace optimalizována pro rychlost - dosáhne se tím vetší propustnosti API
        // Validator.TryValidateObject - byl pomalejší - zjištěno při NBomber testech (OrderAggregator.LoadTests)
        if (!TryBuildOrderBatch(requests, products, out List<Order>? orders, out var errors))
        {
            metrics.RecordBatchRejected();
            return TypedResults.ValidationProblem(errors);
        }

        await store.AddAsync(orders).ConfigureAwait(false);
        metrics.RecordOrdersAccepted(orders.Count);
        return TypedResults.Accepted((string?)null);
    }

    private static bool TryBuildOrderBatch(
        IReadOnlyCollection<OrderRequest> requests,
        IProductRepository products,
        out List<Order> orders,
        out Dictionary<string, string[]> errors)
    {
        orders = new List<Order>(requests.Count);
        Dictionary<string, string[]>? failures = null;
        int index = 0;

        foreach (OrderRequest request in requests)
        {
            if (Validate(request, index, products) is { } failure)
            {
                failures ??= new Dictionary<string, string[]>();

                failures[failure.Key] = new string[] { failure.Message };
            }
            else
            {
                orders.Add(new Order(request.ProductId, request.Quantity));
            }

            index++;
        }

        errors = failures!;
        return failures is null;
    }

    private static (string Key, string Message)? Validate(OrderRequest request, int index, IProductRepository products)
    {
        const int MaxProductIdLength = 64;

        var productId = request.ProductId;

        if (string.IsNullOrEmpty(productId) || productId.Length > MaxProductIdLength)
        {
            return ($"[{index}].productId", ApiMessages.OrderInvalidValue);
        }

        if (request.Quantity < 1)
        {
            return ($"[{index}].quantity", ApiMessages.OrderInvalidValue);
        }

        if (!products.Exists(productId))
        {
            return ($"[{index}].productId",
                string.Format(CultureInfo.CurrentCulture, ApiMessages.OrderUnknownProduct, productId));
        }

        return null;
    }
}
