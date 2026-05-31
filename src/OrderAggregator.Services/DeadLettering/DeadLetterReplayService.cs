using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;
using OrderAggregator.Services.Diagnostics;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Services.DeadLettering;

/// <summary>
/// Background loop that drains the dead-letter store and resends batches downstream.
/// Throttled: at most <see cref="DeadLetterOptions.MaxFilesPerRun"/> files per tick,
/// with <see cref="DeadLetterOptions.ReplayInterval"/> between ticks acting as the
/// backoff. A batch that keeps failing is quarantined after
/// <see cref="DeadLetterOptions.MaxReplayAttempts"/> attempts so it cannot block the
/// queue. Mirrors <c>OrderAggregationFlushService</c>.
/// </summary>
public sealed class DeadLetterReplayService : BackgroundService
{
    private readonly IDeadLetterReader _reader;
    private readonly IAggregatedOrderSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly DeadLetterOptions _options;
    private readonly ILogger<DeadLetterReplayService> _logger;
    private readonly OrderAggregationMetrics? _metrics;

    // Per-file failure counts. In-memory by design: a process restart resets the
    // counters (a poison file simply gets a fresh budget of attempts), which is
    // acceptable because quarantine is a safety net, not an exactly-once guarantee.
    private readonly Dictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public DeadLetterReplayService(
        IDeadLetterReader reader,
        IAggregatedOrderSender sender,
        IOptions<DeadLetterOptions> options,
        ILogger<DeadLetterReplayService> logger,
        TimeProvider? timeProvider = null,
        OrderAggregationMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Dead-letter replay loop started; interval={IntervalSeconds}s maxFilesPerRun={MaxFilesPerRun}",
            _options.ReplayIntervalSeconds,
            _options.MaxFilesPerRun);

        using var timer = new PeriodicTimer(_options.ReplayInterval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await ReplayOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested. No final drain needed: replay holds no in-memory
            // state to lose, it resumes from disk on the next start.
        }
    }

    internal async Task ReplayOnceAsync(CancellationToken cancellationToken)
    {
        var entries = await _reader.ListPendingAsync(_options.MaxFilesPerRun, cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ReplayEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Per-entry isolation: an unexpected failure on one file (e.g. quarantine
                // move denied) must not abort the whole tick or tear down the loop — log
                // it and move on; the file stays pending and is retried next tick. A
                // cancellation (shutdown) is intentionally NOT caught here so it can
                // propagate out and end the loop gracefully.
                _logger.LogError(ex, "Unexpected error replaying dead-letter {File}; skipping this tick", entry.Id);
            }
        }
    }

    private async Task ReplayEntryAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        using var activity = OrderAggregationDiagnostics.ActivitySource.StartActivity("deadletter.replay");
        activity?.SetTag("deadletter.id", entry.Id);

        OrderBatch? batch = await _reader.ReadAsync(entry, cancellationToken).ConfigureAwait(false);
        if (batch is null)
        {
            // Corrupt/unreadable — quarantine immediately, no point retrying.
            activity?.SetStatus(ActivityStatusCode.Error, "corrupt; quarantined");
            await QuarantineAsync(entry, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _sender.SendAsync(batch).ConfigureAwait(false);

            _attempts.Remove(entry.Id);
            await _reader.DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
            _metrics?.RecordReplayed();
            _logger.LogInformation(
                "Replayed dead-letter {File}: products={ProductCount}",
                entry.Id,
                batch.Orders.Count);
        }
        catch (Exception ex)
        {
            var attempt = _attempts[entry.Id] = _attempts.GetValueOrDefault(entry.Id) + 1;
            activity?.SetStatus(ActivityStatusCode.Error, "send failed");

            if (attempt >= _options.MaxReplayAttempts)
            {
                _logger.LogError(
                    ex,
                    "Dead-letter {File} failed replay {Attempt}/{MaxAttempts}; quarantining",
                    entry.Id,
                    attempt,
                    _options.MaxReplayAttempts);
                _attempts.Remove(entry.Id);
                await QuarantineAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Dead-letter {File} failed replay {Attempt}/{MaxAttempts}; will retry next tick",
                    entry.Id,
                    attempt,
                    _options.MaxReplayAttempts);
            }
        }
    }

    private async Task QuarantineAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        await _reader.QuarantineAsync(entry, cancellationToken).ConfigureAwait(false);
        _metrics?.RecordQuarantined();
    }
}
