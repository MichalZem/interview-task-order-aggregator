using Mapster;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Contracts.Products;
using OrderAggregator.Models;

namespace OrderAggregator.Api.Mapping;

public sealed class ContractMappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductDto>();
        config.NewConfig<AggregatedOrder, AggregatedOrderDto>();
        config.NewConfig<OrderBatch, OrderBatchDto>();
    }
}
