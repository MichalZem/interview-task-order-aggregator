using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Services.Diagnostics;
using OrderAggregator.Shared.Configuration;
using OrderAggregator.Models;

namespace OrderAggregator.Services.OrderAggregation;

public sealed class OrderAggregationFlushService : BackgroundService
{
    private readonly IOrderStore _store;
    private readonly IAggregatedOrderSender _sender;
    private readonly IDeadLetterWriter _deadLetter;
    private readonly TimeProvider _timeProvider;
    private readonly AggregationOptions _options;
    private readonly ILogger<OrderAggregationFlushService> _logger;
    private readonly OrderAggregationMetrics? _metrics;

    public OrderAggregationFlushService(
        IOrderStore store,
        IAggregatedOrderSender sender,
        IDeadLetterWriter deadLetter,
        IOptions<AggregationOptions> options,
        ILogger<OrderAggregationFlushService> logger,
        TimeProvider? timeProvider = null,
        OrderAggregationMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _deadLetter = deadLetter ?? throw new ArgumentNullException(nameof(deadLetter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Order aggregation flush loop started; interval={IntervalSeconds}s",
            _options.FlushIntervalSeconds);

        using var timer = new PeriodicTimer(_options.FlushInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await FlushOnceAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }

        _logger.LogInformation("Performing final flush before shutdown");

        await FlushOnceAsync().ConfigureAwait(false);
    }

    internal async Task FlushOnceAsync()
    {
        var snapshot = await DrainStoreAndCreateSnapshotAsync().ConfigureAwait(false);
        if (snapshot is null)
        {
            return; // drain failed, already logged
        }

        if (snapshot.Count == 0)
        {
            _logger.LogDebug("Flush tick: no aggregated orders to send");
            return;
        }

        await SendAggregatedSnapshotAsync(snapshot).ConfigureAwait(false);
    }

    /// <summary>Drain the store; returns null when draining itself failed.</summary>
    private async Task<IReadOnlyCollection<AggregatedOrder>?> DrainStoreAndCreateSnapshotAsync()
    {
        try
        {
            return await _store.SnapshotAndClearAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order store drain failed");
            return null;
        }
    }

    /// <summary>Send one non-empty snapshot, tracing and measuring the attempt
    /// and dead-lettering it if the send can't be made to stick.</summary>
    private async Task SendAggregatedSnapshotAsync(IReadOnlyCollection<AggregatedOrder> snapshot)
    {
        var totalQuantity = snapshot.Sum(o => o.Quantity);
        using var activity = StartFlushActivity(snapshot.Count, totalQuantity);

        var batch = new OrderBatch(snapshot, _timeProvider.GetUtcNow());
        var startTimestamp = _timeProvider.GetTimestamp();
        var delivered = await TrySendAsync(batch).ConfigureAwait(false);

        _metrics?.RecordFlush(snapshot.Count, _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds, delivered);
        activity?.SetTag("aggregation.delivered", delivered);

        if (delivered)
        {
            _logger.LogInformation(
                "Sent aggregated batch: products={ProductCount} totalQuantity={TotalQuantity}",
                snapshot.Count,
                totalQuantity);
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error, "downstream send failed; dead-lettering");
        _logger.LogError(
            "Aggregated batch send failed after {MaxAttempts} attempts; dead-lettering {ProductCount} products",
            _options.SendMaxAttempts,
            snapshot.Count);

        await WriteToDeadLetterAsync(batch).ConfigureAwait(false);
    }

    /// <summary>
    /// Open the flush span with its initial tags. One span per non-empty flush:
    /// the loop runs on a background timer, so ASP.NET Core auto-instrumentation
    /// never sees it — this is the only window into the flush in traces.
    /// </summary>
    private static Activity? StartFlushActivity(int productCount, long totalQuantity)
    {
        var activity = OrderAggregationDiagnostics.ActivitySource.StartActivity("aggregation.flush");
        activity?.SetTag("aggregation.product_count", productCount);
        activity?.SetTag("aggregation.total_quantity", totalQuantity);
        return activity;
    }

    private async Task<bool> TrySendAsync(OrderBatch batch)
    {
        var maxAttempts = Math.Max(1, _options.SendMaxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _sender.SendAsync(batch).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Aggregated batch send failed (attempt {Attempt}/{MaxAttempts})",
                    attempt,
                    maxAttempts);

                if (attempt < maxAttempts && _options.SendRetryDelayMilliseconds > 0)
                {
                    var delay = TimeSpan.FromMilliseconds((long)_options.SendRetryDelayMilliseconds * attempt);
                    await Task.Delay(delay, _timeProvider).ConfigureAwait(false);
                }
            }
        }

        return false;
    }

    private async Task WriteToDeadLetterAsync(OrderBatch batch)
    {
        try
        {
            await _deadLetter.WriteAsync(batch).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to dead-letter aggregated batch after send error; data lost");
        }
    }
}
