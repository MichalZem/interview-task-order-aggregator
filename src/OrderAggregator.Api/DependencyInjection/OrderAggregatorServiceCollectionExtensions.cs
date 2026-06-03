using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderAggregator.Abstractions;
using OrderAggregator.Api.Health;
using OrderAggregator.Shared.Configuration;
using OrderAggregator.Services.DeadLettering;
using OrderAggregator.Services.Diagnostics;
using OrderAggregator.Services.OrderAggregation;
using OrderAggregator.Services.Senders;
using OrderAggregator.Services.Stores;
using StackExchange.Redis;

namespace OrderAggregator.Api.DependencyInjection;

public static class OrderAggregatorServiceCollectionExtensions
{
    public static IServiceCollection AddOrderAggregation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AggregationOptions>()
            .Bind(configuration.GetSection(AggregationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AggregatedOrderSenderOptions>()
            .Bind(configuration.GetSection(AggregatedOrderSenderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DeadLetterOptions>()
            .Bind(configuration.GetSection(DeadLetterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Domain metric instruments. Registered unconditionally (independent of
        // the Observability toggle) so the flush loop and ingest endpoint always
        // resolve them; without an OTel meter provider subscribed, recording is a
        // cheap no-op. AddMetrics supplies the IMeterFactory they depend on.
        services.AddMetrics();
        services.TryAddSingleton<OrderAggregationMetrics>();

        RegisterOrderStore(services, configuration);

        RegisterAggregatedOrderSender(services, configuration);

        services.TryAddSingleton<IDeadLetterWriter, FileDeadLetterWriter>();
        services.TryAddSingleton<IDeadLetterReader, FileDeadLetterReader>();

        services.AddHostedService<OrderAggregationFlushService>();

        // Replay loop resends accumulated dead-letter files. Off-switch via config so
        // it can be disabled where another process owns the dead-letter directory.
        var deadLetterOptions = configuration.GetSection(DeadLetterOptions.SectionName)
            .Get<DeadLetterOptions>() ?? new DeadLetterOptions();
        if (deadLetterOptions.ReplayEnabled)
        {
            services.AddHostedService<DeadLetterReplayService>();
        }

        return services;
    }

    private static void RegisterOrderStore(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OrderStoreOptions>()
            .Bind(configuration.GetSection(OrderStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var storeOptions = configuration.GetSection(OrderStoreOptions.SectionName)
            .Get<OrderStoreOptions>() ?? new OrderStoreOptions();

        switch (storeOptions.Kind)
        {
            case OrderStoreKind.Redis:
                services.TryAddSingleton<IConnectionMultiplexer>(_ =>
                {
                    // AbortOnConnectFail=false so a cold start while Redis is briefly
                    // unreachable does not throw at multiplexer resolution (which would
                    // surface from the readiness probe as a 500). The multiplexer stays in
                    // a retrying state and RedisHealthCheck reports Unhealthy -> 503 until
                    // Redis is reachable, which is the correct readiness signal.
                    var config = ConfigurationOptions.Parse(storeOptions.Redis.ConnectionString);
                    config.AbortOnConnectFail = false;
                    return ConnectionMultiplexer.Connect(config);
                });
                services.TryAddSingleton<IOrderStore, RedisOrderStore>();

                services.AddHealthChecks()
                    .AddCheck<RedisHealthCheck>("redis", tags: [HealthChecksExtensions.ReadyTag]);
                break;

            case OrderStoreKind.Sqlite:
                services.TryAddSingleton<IOrderStore, SqliteOrderStore>();
                break;

            case OrderStoreKind.SqliteGroupCommit:
                services.TryAddSingleton<IOrderStore, SqliteGroupCommitOrderStore>();
                break;

            case OrderStoreKind.InMemory:
            default:
                services.TryAddSingleton<IOrderStore, InMemoryOrderStore>();
                break;
        }
    }

    private static void RegisterAggregatedOrderSender(IServiceCollection services, IConfiguration configuration)
    {
        var senderOptions = configuration.GetSection(AggregatedOrderSenderOptions.SectionName)
            .Get<AggregatedOrderSenderOptions>() ?? new AggregatedOrderSenderOptions();

        switch (senderOptions.Kind)
        {
            case AggregatedOrderSenderKind.Console:
            default:
                services.TryAddSingleton<IAggregatedOrderSender, ConsoleAggregatedOrderSender>();
                break;
        }
    }
}
