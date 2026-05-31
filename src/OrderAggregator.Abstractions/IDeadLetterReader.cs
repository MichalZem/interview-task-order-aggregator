using OrderAggregator.Models;

namespace OrderAggregator.Abstractions;

/// <summary>
/// Read side of the dead-letter store, symmetric to <see cref="IDeadLetterWriter"/>.
/// Drives the replay flow: list pending entries oldest-first, read each back into an
/// <see cref="OrderBatch"/>, then either delete it (resend succeeded) or quarantine it
/// (poison or corrupt).
/// </summary>
public interface IDeadLetterReader
{
    /// <summary>
    /// Pending entries, oldest-first, capped at <paramref name="maxEntries"/>. Excludes
    /// any in-flight writes from <see cref="IDeadLetterWriter"/>.
    /// </summary>
    Task<IReadOnlyList<DeadLetterEntry>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserialize the entry back into a batch. Returns <c>null</c> when the record is
    /// corrupt or unreadable — the caller should quarantine it.
    /// </summary>
    Task<OrderBatch?> ReadAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Remove the entry after a successful replay.</summary>
    Task DeleteAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Move the entry into quarantine (attempts exhausted or corrupt payload).</summary>
    Task QuarantineAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);
}
