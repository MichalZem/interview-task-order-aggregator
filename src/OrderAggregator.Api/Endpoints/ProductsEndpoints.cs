using MapsterMapper;
using Microsoft.AspNetCore.Http.HttpResults;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Products;
using OrderAggregator.Models;

namespace OrderAggregator.Api.Endpoints;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products")
            .WithTags("Products")
            .RequireAuthorization();

        group.MapGet("/", ListProducts)
            .WithName("ListProducts")
            .WithSummary("Returns all products from the currently loaded catalog")
            .WithDescription(
                "Returns a snapshot of the catalog that `POST /api/orders` validates incoming orders " +
                "against. The catalog is loaded once at startup from the configured JSON file " +
                "(`ProductCatalog:FilePath`), so the result is deterministic across requests and file " +
                "changes require a process restart.")
            .Produces<ProductListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{productId}", GetProduct)
            .WithName("GetProduct")
            .WithSummary("Returns a single product by ID")
            .Produces<ProductDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static Ok<ProductListResponse> ListProducts(IProductRepository products, IMapper mapper)
    {
        List<ProductDto> items = mapper.Map<List<ProductDto>>(products.GetAll());
        
        return TypedResults.Ok(new ProductListResponse(items.Count, items));
    }

    private static Results<Ok<ProductDto>, NotFound> GetProduct(string productId, IProductRepository products, IMapper mapper)
    {
        Product? product = products.Find(productId);
        
        return product is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(mapper.Map<ProductDto>(product));
    }
}
