using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Selects which <c>IAggregatedOrderSender</c> implementation handles each
/// flushed batch. Bound from configuration section <c>AggregatedOrderSender</c>.
/// </summary>
public sealed class AggregatedOrderSenderOptions
{
    public const string SectionName = "AggregatedOrderSender";

    [Required]
    public AggregatedOrderSenderKind Kind { get; set; } = AggregatedOrderSenderKind.Console;

    /// <summary>Console-sender settings, used only when <see cref="Kind"/> is <c>Console</c>.</summary>
    public ConsoleSenderOptions Console { get; set; } = new();
}

public enum AggregatedOrderSenderKind
{
    /// <summary>Pretty-prints the aggregated batch to the logger. Default for dev / demos.</summary>
    Console,
}

/// <summary>Configuration for <c>ConsoleAggregatedOrderSender</c>.</summary>
public sealed class ConsoleSenderOptions
{
    /// <summary>
    /// Probability in [0,1] that a send is made to fail on purpose, so the
    /// dead-letter path can be exercised in dev. 0 = never fail (production
    /// default); 0.5 = fail roughly half the batches.
    /// </summary>
    [Range(0d, 1d)]
    public double FailureProbability { get; set; }
}
