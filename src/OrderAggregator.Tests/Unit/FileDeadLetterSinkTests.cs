using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderAggregator.Models;
using OrderAggregator.Services.DeadLettering;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class FileDeadLetterSinkTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "oa-deadletter-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAsync_CreatesDirectory_AndWritesBatchAsJson()
    {
        var sink = CreateSink();
        var batch = new OrderBatch(
            new[] { new AggregatedOrder("a", 5), new AggregatedOrder("b", 2) },
            new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero));

        await sink.WriteAsync(batch);

        var files = Directory.GetFiles(_directory, "*.json");
        var file = Assert.Single(files);

        // Round-trips with the same web/camelCase shape the senders emit.
        var roundTripped = JsonSerializer.Deserialize<OrderBatch>(
            await File.ReadAllTextAsync(file),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(5, roundTripped!.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Equal(2, roundTripped.Orders.Single(o => o.ProductId == "b").Quantity);
    }

    [Fact]
    public async Task WriteAsync_LeavesNoTempFiles_AfterWrite()
    {
        var sink = CreateSink();

        await sink.WriteAsync(new OrderBatch(
            new[] { new AggregatedOrder("a", 1) },
            DateTimeOffset.UtcNow));

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private FileDeadLetterWriter CreateSink()
    {
        var options = Options.Create(new DeadLetterOptions { Directory = _directory });
        return new FileDeadLetterWriter(options, NullLogger<FileDeadLetterWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
