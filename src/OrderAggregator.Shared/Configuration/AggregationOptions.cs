using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Tunables for the periodic aggregation/flush loop. Bound from configuration
/// section <c>Aggregation</c>.
/// </summary>
public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    /// <summary>
    /// Minimum interval between two sends, in seconds. Lower bound is 20 to honor
    /// the domain contract ("aggregate and send no more often than once per 20s").
    /// </summary>
    [Range(20, 3600)]
    public int FlushIntervalSeconds { get; set; } = 20;

    /// <summary>
    /// How many times to attempt a downstream send before dead-lettering the
    /// batch. 1 = no retry. Retries absorb transient downstream blips so a brief
    /// outage doesn't park otherwise-deliverable batches.
    /// </summary>
    [Range(1, 10)]
    public int SendMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay between send retries, in milliseconds; grows linearly per
    /// attempt (attempt 1 → 1×, attempt 2 → 2×, …). 0 = retry immediately.
    /// </summary>
    [Range(0, 60000)]
    public int SendRetryDelayMilliseconds { get; set; } = 200;

    public TimeSpan FlushInterval => TimeSpan.FromSeconds(FlushIntervalSeconds);
}
