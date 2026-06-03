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
        TimeProvider timeProvider,
        OrderAggregationMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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
                // Per-entry isolation: an unexpected failure on one file (e.g. a payload
                // that escapes ReadAsync's filter, or a denied quarantine move) must not
                // abort the tick or tear down the loop. Count it against the same attempt
                // budget as a failed send so a persistently broken file is eventually
                // quarantined instead of being retried forever. A cancellation (shutdown)
                // is intentionally NOT caught so it can propagate and end the loop.
                await RegisterFailedAttemptAsync(entry, ex, "unexpected error", cancellationToken).ConfigureAwait(false);
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
            activity?.SetStatus(ActivityStatusCode.Error, "send failed");
            await RegisterFailedAttemptAsync(entry, ex, "send failed", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Count one failed attempt for the entry and quarantine it once the attempt budget
    /// (<see cref="DeadLetterOptions.MaxReplayAttempts"/>) is exhausted. Shared by a failed
    /// downstream send and by any unexpected per-entry error, so every kind of persistent
    /// failure converges on quarantine instead of being retried forever. Never throws: a
    /// failed quarantine move is logged and the entry is left for a later tick.
    /// </summary>
    private async Task RegisterFailedAttemptAsync(DeadLetterEntry entry, Exception ex, string reason, CancellationToken cancellationToken)
    {
        var attempt = _attempts[entry.Id] = _attempts.GetValueOrDefault(entry.Id) + 1;

        if (attempt < _options.MaxReplayAttempts)
        {
            _logger.LogWarning(
                ex,
                "Dead-letter {File} replay failed ({Reason}) {Attempt}/{MaxAttempts}; will retry next tick",
                entry.Id,
                reason,
                attempt,
                _options.MaxReplayAttempts);
            return;
        }

        _logger.LogError(
            ex,
            "Dead-letter {File} replay failed ({Reason}) {Attempt}/{MaxAttempts}; quarantining",
            entry.Id,
            reason,
            attempt,
            _options.MaxReplayAttempts);
        _attempts.Remove(entry.Id);

        try
        {
            await QuarantineAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception quarantineEx) when (!cancellationToken.IsCancellationRequested)
        {
            // Couldn't move the file aside (e.g. permission denied). Don't let it tear down
            // the loop; it stays pending and the quarantine is retried on a later tick.
            _logger.LogError(quarantineEx, "Failed to quarantine dead-letter {File}", entry.Id);
        }
    }

    private async Task QuarantineAsync(DeadLetterEntry entry, CancellationToken cancellationToken)
    {
        await _reader.QuarantineAsync(entry, cancellationToken).ConfigureAwait(false);
        _metrics?.RecordQuarantined();
    }
}
