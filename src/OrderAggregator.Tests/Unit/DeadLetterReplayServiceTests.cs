using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Models;
using OrderAggregator.Services.DeadLettering;
using OrderAggregator.Shared.Configuration;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class DeadLetterReplayServiceTests
{
    [Fact]
    public async Task ReplayOnce_ResendsAndDeletes_OnSuccess()
    {
        var reader = new FakeDeadLetterReader();
        reader.Add("deadletter-1.json", Batch(("a", 5)));
        var sender = new CapturingSender();
        var service = CreateService(reader, sender);

        await service.ReplayOnceAsync(CancellationToken.None);

        var sent = Assert.Single(sender.Batches);
        Assert.Equal(5, sent.Orders.Single(o => o.ProductId == "a").Quantity);
        Assert.Contains("deadletter-1.json", reader.Deleted);
        Assert.Empty(reader.Quarantined);
    }

    [Fact]
    public async Task ReplayOnce_QuarantinesAfterMaxAttempts_WhenSenderAlwaysFails()
    {
        var reader = new FakeDeadLetterReader();
        reader.Add("deadletter-1.json", Batch(("a", 1)));
        var sender = new AlwaysFailingSender();
        var service = CreateService(reader, sender, maxReplayAttempts: 3);

        // First (maxAttempts - 1) ticks just retry, no quarantine yet.
        await service.ReplayOnceAsync(CancellationToken.None);
        await service.ReplayOnceAsync(CancellationToken.None);
        Assert.Empty(reader.Quarantined);

        // The attempt that reaches the cap quarantines the entry.
        await service.ReplayOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "deadletter-1.json" }, reader.Quarantined);

        // Quarantined => no longer pending => not retried again.
        await service.ReplayOnceAsync(CancellationToken.None);
        Assert.Single(reader.Quarantined);
        Assert.Empty(reader.Deleted);
    }

    [Fact]
    public async Task ReplayOnce_QuarantinesCorruptEntry_WithoutSending()
    {
        var reader = new FakeDeadLetterReader();
        reader.Add("deadletter-bad.json", batch: null); // corrupt => ReadAsync returns null
        var sender = new CapturingSender();
        var service = CreateService(reader, sender);

        await service.ReplayOnceAsync(CancellationToken.None);

        Assert.Empty(sender.Batches);
        Assert.Equal(new[] { "deadletter-bad.json" }, reader.Quarantined);
    }

    [Fact]
    public async Task ReplayOnce_ProcessesAtMostMaxFilesPerRun()
    {
        var reader = new FakeDeadLetterReader();
        for (var i = 0; i < 5; i++)
        {
            reader.Add($"deadletter-{i}.json", Batch(("a", 1)));
        }
        var sender = new CapturingSender();
        var service = CreateService(reader, sender, maxFilesPerRun: 2);

        await service.ReplayOnceAsync(CancellationToken.None);

        Assert.Equal(2, sender.Batches.Count);
        Assert.Equal(2, reader.Deleted.Count);
    }

    private static DeadLetterReplayService CreateService(
        IDeadLetterReader reader,
        IAggregatedOrderSender sender,
        int maxFilesPerRun = 10,
        int maxReplayAttempts = 5)
    {
        var options = Options.Create(new DeadLetterOptions
        {
            MaxFilesPerRun = maxFilesPerRun,
            MaxReplayAttempts = maxReplayAttempts,
        });
        return new DeadLetterReplayService(
            reader,
            sender,
            options,
            NullLogger<DeadLetterReplayService>.Instance,
            TimeProvider.System);
    }

    private static OrderBatch Batch(params (string ProductId, long Quantity)[] orders) =>
        new(Guid.NewGuid(), orders.Select(o => new AggregatedOrder(o.ProductId, o.Quantity)).ToList(), DateTimeOffset.UtcNow);

    private sealed class FakeDeadLetterReader : IDeadLetterReader
    {
        private readonly List<(string Id, OrderBatch? Batch)> _entries = new();
        public List<string> Deleted { get; } = new();
        public List<string> Quarantined { get; } = new();

        public void Add(string id, OrderBatch? batch) => _entries.Add((id, batch));

        public Task<IReadOnlyList<DeadLetterEntry>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DeadLetterEntry> result = _entries
                .Take(maxEntries)
                .Select(e => new DeadLetterEntry(e.Id))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<OrderBatch?> ReadAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.First(e => e.Id == entry.Id).Batch);

        public Task DeleteAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            Deleted.Add(entry.Id);
            _entries.RemoveAll(e => e.Id == entry.Id);
            return Task.CompletedTask;
        }

        public Task QuarantineAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
        {
            Quarantined.Add(entry.Id);
            _entries.RemoveAll(e => e.Id == entry.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingSender : IAggregatedOrderSender
    {
        public List<OrderBatch> Batches { get; } = new();

        public Task SendAsync(OrderBatch batch)
        {
            Batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailingSender : IAggregatedOrderSender
    {
        public Task SendAsync(OrderBatch batch) =>
            throw new InvalidOperationException("Simulated send failure");
    }
}
