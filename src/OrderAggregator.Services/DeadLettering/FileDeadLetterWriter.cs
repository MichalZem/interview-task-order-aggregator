using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Services.DeadLettering;

public sealed class FileDeadLetterWriter : IDeadLetterWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly DeadLetterOptions _options;
    private readonly ILogger<FileDeadLetterWriter> _logger;

    public FileDeadLetterWriter(IOptions<DeadLetterOptions> options, ILogger<FileDeadLetterWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task WriteAsync(OrderBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        Directory.CreateDirectory(_options.Directory);

        // BatchId in the name ties the file to the idempotency key sent downstream; the
        // FlushedAt prefix keeps the directory listing sorted FIFO for the replay loop.
        var fileName = $"deadletter-{batch.FlushedAt:yyyyMMdd-HHmmss-fff}-{batch.BatchId:N}.json";
        var path = Path.Combine(_options.Directory, fileName);
        var tempPath = path + ".tmp";

        var json = JsonSerializer.Serialize(batch, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
        File.Move(tempPath, path);

        _logger.LogWarning(
            "Aggregated batch dead-lettered to {Path}: products={ProductCount}",
            path,
            batch.Orders.Count);
    }
}
