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
}
