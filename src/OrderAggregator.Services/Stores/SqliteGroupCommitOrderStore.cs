using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OrderAggregator.Abstractions;
using OrderAggregator.Shared.Configuration;

using Order = OrderAggregator.Models.Order;
using AggregatedOrder = OrderAggregator.Models.AggregatedOrder;

namespace OrderAggregator.Services.Stores;

/// <summary>
/// SQLite buffer with <b>group commit</b>: same single file and the same
/// <c>synchronous=FULL</c> durability as <see cref="SqliteOrderStore"/>, but a
/// single writer thread coalesces all requests that arrive while a commit is in
/// flight into <i>one</i> transaction → one fsync amortized across many requests.
/// That lifts the throughput ceiling well above the per-request-fsync limit
/// without weakening durability: a request's <c>AddAsync</c> only completes once
/// its data is committed to disk (so a client never gets an ack for data that was
/// then lost — the lost write simply isn't acked and is retried).
/// <para>
/// All connection access goes through the writer thread (adds and snapshots are
/// queued as work items), so the connection is single-threaded without a lock.
/// </para>
/// </summary>
public sealed class SqliteGroupCommitOrderStore : IOrderStore, IAsyncDisposable
{
    private readonly Channel<WriteOp> _channel =
        Channel.CreateUnbounded<WriteOp>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SqliteConnection _connection;
    private readonly Task _writerLoop;

    public SqliteGroupCommitOrderStore(IOptions<OrderStoreOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connection = SqliteBuffer.OpenInitialized(options.Value.Sqlite.DataSource);
        _writerLoop = Task.Run(RunWriterAsync);
    }

    public ValueTask AddAsync(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var op = new AddOp(orders);
        if (!_channel.Writer.TryWrite(op))
        {
            throw new InvalidOperationException("Order store is shutting down.");
        }

        // Completes only after the batch this request joined is committed to disk.
        return new ValueTask(op.Completion.Task);
    }

    public ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync()
    {
        var op = new SnapshotOp();
        if (!_channel.Writer.TryWrite(op))
        {
            throw new InvalidOperationException("Order store is shutting down.");
        }

        return new ValueTask<IReadOnlyCollection<AggregatedOrder>>(op.Completion.Task);
    }

    public async ValueTask DisposeAsync()
    {
        // Stop accepting work, let the writer drain everything already queued, then
        // close the connection — no in-flight request is dropped on graceful stop.
        _channel.Writer.Complete();
        await _writerLoop.ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunWriterAsync()
    {
        var reader = _channel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var adds = new List<AddOp>();
            SnapshotOp? snapshot = null;

            // Drain everything available right now. Coalesce consecutive adds into
            // one transaction; stop at a snapshot so FIFO ordering is preserved
            // (adds queued before it are committed first, ones after it next loop).
            while (reader.TryRead(out var op))
            {
                if (op is AddOp add)
                {
                    adds.Add(add);
                }
                else
                {
                    snapshot = (SnapshotOp)op;
                    break;
                }
            }

            if (adds.Count > 0)
            {
                await FlushAddsAsync(adds).ConfigureAwait(false);
            }

            if (snapshot is not null)
            {
                await RunSnapshotAsync(snapshot).ConfigureAwait(false);
            }
        }
    }

    private async Task FlushAddsAsync(List<AddOp> adds)
    {
        try
        {
            // One transaction (= one fsync) for every queued request's orders.
            await SqliteBuffer.ApplyAsync(_connection, adds.SelectMany(a => a.Orders)).ConfigureAwait(false);
            foreach (var add in adds)
            {
                add.Completion.TrySetResult();
            }
        }
        catch (Exception ex)
        {
            // Fail the whole group together — those requests aren't acked, so the
            // increments aren't double-counted; clients retry with the same BatchId.
            foreach (var add in adds)
            {
                add.Completion.TrySetException(ex);
            }
        }
    }

    private async Task RunSnapshotAsync(SnapshotOp snapshot)
    {
        try
        {
            var result = await SqliteBuffer.SnapshotAndClearAsync(_connection).ConfigureAwait(false);
            snapshot.Completion.TrySetResult(result);
        }
        catch (Exception ex)
        {
            snapshot.Completion.TrySetException(ex);
        }
    }

    private abstract class WriteOp;

    private sealed class AddOp(IEnumerable<Order> orders) : WriteOp
    {
        public IEnumerable<Order> Orders { get; } = orders;

        // RunContinuationsAsynchronously: never run an awaiting request's continuation
        // inline on the writer thread, or it would stall the writer.
        public TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SnapshotOp : WriteOp
    {
        public TaskCompletionSource<IReadOnlyCollection<AggregatedOrder>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
