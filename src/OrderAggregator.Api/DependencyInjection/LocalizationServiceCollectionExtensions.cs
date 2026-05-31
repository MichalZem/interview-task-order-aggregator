using OrderAggregator.Shared.Const;

namespace OrderAggregator.Api.DependencyInjection;

public static class LocalizationServiceCollectionExtensions
{
    public static IServiceCollection AddAppLocalization(this IServiceCollection services)
    {
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.SetDefaultCulture(LocalizationConstants.DefaultCulture)
                .AddSupportedCultures(LocalizationConstants.SupportedCultures)
                .AddSupportedUICultures(LocalizationConstants.SupportedCultures);
        });

        return services;
    }
}
