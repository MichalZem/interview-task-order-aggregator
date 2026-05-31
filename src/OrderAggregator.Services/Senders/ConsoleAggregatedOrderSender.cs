using System.Text.Json;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Models;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Services.Senders;

public sealed class ConsoleAggregatedOrderSender : IAggregatedOrderSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly IMapper _mapper;
    private readonly ILogger<ConsoleAggregatedOrderSender> _logger;
    private readonly double _failureProbability;

    public ConsoleAggregatedOrderSender(
        IMapper mapper,
        IOptions<AggregatedOrderSenderOptions> options,
        ILogger<ConsoleAggregatedOrderSender> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _failureProbability = options.Value.Console.FailureProbability;
    }

    public Task SendAsync(OrderBatch batch)
    {
        // Optional fault injection so the dead-letter path can be exercised in dev.
        if (_failureProbability > 0 && Random.Shared.NextDouble() < _failureProbability)
        {
            throw new InvalidOperationException(
                "Simulated downstream send failure (ConsoleSender:FailureProbability).");
        }

        var dto = _mapper.Map<OrderBatchDto>(batch);
        var json = JsonSerializer.Serialize(dto, JsonOptions);

        _logger.LogInformation("Aggregated batch sent to console sink:\n{Json}", json);

        return Task.CompletedTask;
    }
}
