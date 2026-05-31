using Mapster;
using MapsterMapper;
using OrderAggregator.Api.Mapping;

namespace OrderAggregator.Api.DependencyInjection;

public static class MappingServiceCollectionExtensions
{
    public static IServiceCollection AddContractMapping(this IServiceCollection services)
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(typeof(ContractMappingRegister).Assembly);
        config.Compile();

        services.AddSingleton(config);
        services.AddSingleton<IMapper>(sp => new Mapper(sp.GetRequiredService<TypeAdapterConfig>()));

        return services;
    }
}
