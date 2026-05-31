using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Api.DependencyInjection;

public static class RequestLimitsServiceCollectionExtensions
{
    public static IServiceCollection AddRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RequestLimitsOptions>()
            .Bind(configuration.GetSection(RequestLimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<RequestLimitsOptions>>((kestrel, limits) =>
            {
                kestrel.Limits.MaxRequestBodySize = limits.Value.MaxRequestBodyBytes;
            });

        return services;
    }
}
