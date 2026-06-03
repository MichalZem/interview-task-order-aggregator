using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderAggregator.Models;
using OrderAggregator.Services.DeadLettering;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class FileDeadLetterReaderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "oa-deadletter-reader-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListPendingAsync_ReturnsFilesOldestFirst()
    {
        // Arrange
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), ("a", 1));
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 11, 0, 0, TimeSpan.Zero), ("b", 2));
        var reader = CreateReader();

        // Act
        var entries = await reader.ListPendingAsync(10);

        // Assert
        Assert.Equal(2, entries.Count);
        // FIFO: the earlier FlushedAt sorts first by its filename timestamp prefix.
        Assert.True(string.CompareOrdinal(entries[0].Id, entries[1].Id) < 0);
        var first = await reader.ReadAsync(entries[0]);
        Assert.NotNull(first);
        Assert.Equal(1, first!.Orders.Single(o => o.ProductId == "a").Quantity);
    }

    [Fact]
    public async Task ListPendingAsync_IgnoresTempFiles()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "deadletter-20260531-100000-000-abc.json.tmp"), "{}");
        var reader = CreateReader();

        // Act
        var entries = await reader.ListPendingAsync(10);

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public async Task ListPendingAsync_RespectsMaxEntries()
    {
        // Arrange
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), ("a", 1));
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 11, 0, 0, TimeSpan.Zero), ("b", 2));
        var reader = CreateReader();

        // Act
        var entries = await reader.ListPendingAsync(1);

        // Assert
        Assert.Single(entries);
    }

    [Fact]
    public async Task ReadAsync_RoundTripsBatch()
    {
        // Arrange
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), ("a", 5), ("b", 2));
        var reader = CreateReader();
        var entry = Assert.Single(await reader.ListPendingAsync(10));

        // Act
        var batch = await reader.ReadAsync(entry);

        // Assert
        Assert.NotNull(batch);
        Assert.Equal(5, batch!.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(2, batch.Orders.Single(o => o.ProductId == "b").Quantity);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNull_ForCorruptFile()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "deadletter-20260531-100000-000-bad.json"), "not json at all");
        var reader = CreateReader();
        var entry = Assert.Single(await reader.ListPendingAsync(10));

        // Act & Assert
        Assert.Null(await reader.ReadAsync(entry));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        // Arrange
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), ("a", 1));
        var reader = CreateReader();
        var entry = Assert.Single(await reader.ListPendingAsync(10));

        // Act
        await reader.DeleteAsync(entry);

        // Assert
        Assert.Empty(await reader.ListPendingAsync(10));
    }

    [Fact]
    public async Task QuarantineAsync_MovesFileToPoisonSubdirectory()
    {
        // Arrange
        await WriteBatchAsync(new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero), ("a", 1));
        var reader = CreateReader();
        var entry = Assert.Single(await reader.ListPendingAsync(10));

        // Act
        await reader.QuarantineAsync(entry);

        // Assert
        // Gone from the pending queue, present in poison/.
        Assert.Empty(await reader.ListPendingAsync(10));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, "poison"), "*.json"));
    }

    private async Task WriteBatchAsync(DateTimeOffset flushedAt, params (string ProductId, long Quantity)[] orders)
    {
        var options = Options.Create(new DeadLetterOptions { Directory = _directory });
        var writer = new FileDeadLetterWriter(options, NullLogger<FileDeadLetterWriter>.Instance);
        var batch = new OrderBatch(
            Guid.NewGuid(),
            orders.Select(o => new AggregatedOrder(o.ProductId, o.Quantity)).ToList(),
            flushedAt);
        await writer.WriteAsync(batch);
    }

    private FileDeadLetterReader CreateReader()
    {
        var options = Options.Create(new DeadLetterOptions { Directory = _directory });
        return new FileDeadLetterReader(options, NullLogger<FileDeadLetterReader>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
