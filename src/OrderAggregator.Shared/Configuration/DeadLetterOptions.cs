using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Configuration for the file-backed dead-letter sink. Bound from configuration
/// section <c>DeadLetter</c>. Batches that fail to send are serialized to JSON
/// files under <see cref="Directory"/>.
/// </summary>
public sealed class DeadLetterOptions
{
    public const string SectionName = "DeadLetter";

    /// <summary>
    /// Target directory for dead-lettered batch files. Created on first write if
    /// missing. A relative path resolves against the process working directory.
    /// </summary>
    [Required]
    [StringLength(260, MinimumLength = 1)]
    public string Directory { get; set; } = "DeadLetter";

    /// <summary>
    /// Master switch for the background replay loop that resends dead-lettered batches.
    /// When <c>false</c>, files accumulate untouched (no <c>DeadLetterReplayService</c>
    /// is registered).
    /// </summary>
    public bool ReplayEnabled { get; set; } = true;

    /// <summary>Seconds between replay ticks. This interval is the backoff between attempts.</summary>
    [Range(1, 3600)]
    public int ReplayIntervalSeconds { get; set; } = 30;

    /// <summary>Maximum dead-letter files processed per replay tick — throttles the drain rate.</summary>
    [Range(1, 1000)]
    public int MaxFilesPerRun { get; set; } = 10;

    /// <summary>Failed send attempts for a single batch before it is quarantined as poison.</summary>
    [Range(1, 100)]
    public int MaxReplayAttempts { get; set; } = 5;

    /// <summary>
    /// Sub-directory (resolved under <see cref="Directory"/>) where poison and corrupt
    /// batches are moved so they stop blocking the replay queue but remain for inspection.
    /// </summary>
    [Required]
    [StringLength(260, MinimumLength = 1)]
    public string PoisonDirectory { get; set; } = "poison";

    /// <summary>Convenience projection of <see cref="ReplayIntervalSeconds"/>.</summary>
    public TimeSpan ReplayInterval => TimeSpan.FromSeconds(ReplayIntervalSeconds);
}
