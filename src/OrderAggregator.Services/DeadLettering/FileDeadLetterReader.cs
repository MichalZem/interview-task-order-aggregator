using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Services.DeadLettering;

/// <summary>
/// Read side of the file-backed dead-letter store. Enumerates the JSON files written
/// by <see cref="FileDeadLetterWriter"/>, oldest-first, and supports deleting (on a
/// successful replay) or quarantining (poison/corrupt) them.
/// </summary>
public sealed class FileDeadLetterReader : IDeadLetterReader
{
    // Match the writer's filename prefix. The pattern must end in ".json" so the
    // writer's in-flight temp files ("deadletter-….json.tmp") are NOT picked up —
    // keep this constraint if the glob is ever changed.
    private const string PendingSearchPattern = "deadletter-*.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DeadLetterOptions _options;
    private readonly ILogger<FileDeadLetterReader> _logger;

    public FileDeadLetterReader(IOptions<DeadLetterOptions> options, ILogger<FileDeadLetterReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<DeadLetterEntry>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        if (maxEntries <= 0 || !Directory.Exists(_options.Directory))
        {
            return Task.FromResult<IReadOnlyList<DeadLetterEntry>>([]);
        }

        // The filename timestamp prefix (yyyyMMdd-HHmmss-fff) sorts lexicographically =
        // chronologically, so ordering by name drains the backlog FIFO.
        var entries = Directory.EnumerateFiles(_options.Directory, PendingSearchPattern)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(maxEntries)
            .Select(name => new DeadLetterEntry(name!))
            .ToList();

        return Task.FromResult<IReadOnlyList<DeadLetterEntry>>(entries);
    }

    public async Task<OrderBatch?> ReadAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var path = ResolvePath(entry);

        try
        {
            await using var stream = File.OpenRead(path);
            var batch = await JsonSerializer
                .DeserializeAsync<OrderBatch>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (batch?.Orders is null)
            {
                _logger.LogWarning("Dead-letter file {File} deserialized to an empty/invalid batch", entry.Id);
                return null;
            }

            return batch;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Failed to read dead-letter file {File}; treating as corrupt", entry.Id);
            return null;
        }
    }

    public Task DeleteAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            File.Delete(ResolvePath(entry));
        }
        catch (FileNotFoundException)
        {
            // Already gone — nothing to do.
        }

        return Task.CompletedTask;
    }

    public Task QuarantineAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var source = ResolvePath(entry);
        var poisonDir = Path.Combine(_options.Directory, _options.PoisonDirectory);
        Directory.CreateDirectory(poisonDir);

        var destination = Path.Combine(poisonDir, entry.Id);
        if (File.Exists(destination))
        {
            // Avoid clobbering a previously quarantined file with the same name.
            destination = Path.Combine(poisonDir, $"{Path.GetFileNameWithoutExtension(entry.Id)}-{Guid.NewGuid():N}{Path.GetExtension(entry.Id)}");
        }

        try
        {
            File.Move(source, destination);
            _logger.LogWarning("Dead-letter file {File} quarantined to {Destination}", entry.Id, destination);
        }
        catch (FileNotFoundException)
        {
            // Already gone — nothing to quarantine.
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(DeadLetterEntry entry) => Path.Combine(_options.Directory, entry.Id);
}
