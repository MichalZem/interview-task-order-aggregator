namespace OrderAggregator.Api.Endpoints;

public static class SetupApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        app.MapOrdersEndpoints();

        app.MapProductsEndpoints();
    }
}
