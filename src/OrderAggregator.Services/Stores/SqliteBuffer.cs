using Microsoft.Data.Sqlite;

using Order = OrderAggregator.Models.Order;
using AggregatedOrder = OrderAggregator.Models.AggregatedOrder;

namespace OrderAggregator.Services.Stores;

/// <summary>
/// Low-level SQLite buffer operations shared by the SQLite-backed stores. Holds the
/// schema, the SQL, and the connection setup in one place so the write-through
/// (<see cref="SqliteOrderStore"/>) and group-commit
/// (<see cref="SqliteGroupCommitOrderStore"/>) variants differ only in <i>how</i> they
/// serialize access to the connection, not in the SQL they run.
/// </summary>
internal static class SqliteBuffer
{
    private const string UpsertSql =
        "INSERT INTO buffer(product_id, quantity) VALUES($id, $qty) " +
        "ON CONFLICT(product_id) DO UPDATE SET quantity = quantity + excluded.quantity;";

    private const string SelectAllSql = "SELECT product_id, quantity FROM buffer;";
    private const string DeleteAllSql = "DELETE FROM buffer;";

    /// <summary>
    /// Opens a connection to the buffer file and prepares its schema. WAL +
    /// <c>synchronous=FULL</c> = fsync on every commit (maximum durability). Run
    /// eagerly at startup so a bad path or unwritable file fails fast.
    /// </summary>
    public static SqliteConnection OpenInitialized(string dataSource)
    {
        EnsureDirectoryExists(dataSource);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS buffer(
                product_id TEXT PRIMARY KEY,
                quantity   INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// Applies every order as an UPSERT inside one transaction → a single commit
    /// (one fsync). The caller decides how many requests' orders to pass at once:
    /// one request (write-through) or many coalesced requests (group commit).
    /// </summary>
    public static async ValueTask ApplyAsync(SqliteConnection connection, IEnumerable<Order> orders)
    {
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = UpsertSql;
        var idParam = command.Parameters.Add("$id", SqliteType.Text);
        var qtyParam = command.Parameters.Add("$qty", SqliteType.Integer);

        foreach (var order in orders)
        {
            idParam.Value = order.ProductId;
            qtyParam.Value = order.Quantity;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically reads the whole buffer and clears it in one transaction — no
    /// increment can slip in between the read and the reset, so nothing is lost.
    /// </summary>
    public static async ValueTask<IReadOnlyCollection<AggregatedOrder>> SnapshotAndClearAsync(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();

        var snapshot = new List<AggregatedOrder>();
        using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = SelectAllSql;
            using var reader = await selectCommand.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                // INTEGER is 64-bit, so totals above int.MaxValue round-trip intact.
                snapshot.Add(new AggregatedOrder(reader.GetString(0), reader.GetInt64(1)));
            }
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = DeleteAllSql;
            await deleteCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);

        return snapshot.Count == 0
            ? Array.Empty<AggregatedOrder>()
            : snapshot;
    }

    private static void EnsureDirectoryExists(string dataSource)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
