using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Shared.Configuration;
using OrderAggregator.Services.Products;

namespace OrderAggregator.Api.DependencyInjection;

public static class ProductCatalogServiceCollectionExtensions
{
    public static IServiceCollection AddProductCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductCatalogOptions>()
            .Bind(configuration.GetSection(ProductCatalogOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IProductRepository>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ProductCatalogOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<JsonFileProductRepository>>();
            var environment = sp.GetRequiredService<IHostEnvironment>();

            var path = Path.IsPathRooted(options.FilePath)
                ? options.FilePath
                : Path.Combine(environment.ContentRootPath, options.FilePath);

            return JsonFileProductRepository.LoadFromFile(path, logger);
        });

        return services;
    }

    public static WebApplication EnsureProductCatalogLoaded(this WebApplication app)
    {
        app.Services.GetRequiredService<IProductRepository>();
        return app;
    }
}
