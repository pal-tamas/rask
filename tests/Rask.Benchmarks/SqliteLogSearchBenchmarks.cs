using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;

namespace Rask.Benchmarks;

// The dashboard's log search (#1111), both ways, on the schema SqliteLogStore writes. Each arm does what
// SearchAsync does for one page: count the matches, then read the newest 50.
//
//   Like     — (Message LIKE '%s%' OR Exception LIKE '%s%'): every retained row read, every search.
//   Trigram  — Id IN (SELECT rowid FROM RaskLogSearch WHERE RaskLogSearch MATCH '"s"'): the FTS5 trigram index.
//
// The substring is an order id that appears in about 1 row in 1,000 — a search box is most useful, and a scan
// most expensive, exactly when what you look for is rare. Append measures the price the index adds to the write
// path: 100 entries in one transaction, into a table with and without the index's triggers.
[MemoryDiagnoser]
public class SqliteLogSearchBenchmarks
{
    private const string Needle = "ORD-7F3A91";

    private string _indexed = null!;
    private string _plain = null!;
    private int _next;

    [Params(100_000, 500_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _indexed = Create(withIndex: true);
        _plain = Create(withIndex: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_indexed);
        File.Delete(_plain);
    }

    [Benchmark(Baseline = true)]
    public int Like() => Page(
        _plain,
        @"(Message LIKE $s ESCAPE '\' OR Exception LIKE $s ESCAPE '\')",
        "%" + Needle + "%");

    [Benchmark]
    public int Trigram() => Page(
        _indexed,
        "Id IN (SELECT rowid FROM RaskLogSearch WHERE RaskLogSearch MATCH $s)",
        "\"" + Needle + "\"");

    [Benchmark]
    public void AppendWithIndex() => Append(_indexed);

    [Benchmark]
    public void AppendWithoutIndex() => Append(_plain);

    private static int Page(string path, string where, string search)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM RaskLog WHERE {where};";
        count.Parameters.AddWithValue("$s", search);
        var total = Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        using var page = connection.CreateCommand();
        page.CommandText = $"SELECT Id, Message FROM RaskLog WHERE {where} ORDER BY Id DESC LIMIT 50;";
        page.Parameters.AddWithValue("$s", search);
        using var reader = page.ExecuteReader();
        while (reader.Read())
        {
        }

        return total;
    }

    private void Append(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO RaskLog (Timestamp, Level, Category, EventId, Message, Exception) VALUES ('2026-09-21T10:00:00Z', 2, 'Shop.Orders', 0, $m, NULL);";
        var message = insert.Parameters.Add("$m", SqliteType.Text);
        for (var i = 0; i < 100; i++)
        {
            message.Value = $"Order {++_next} shipped to warehouse 3 after payment was captured";
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private string Create(bool withIndex)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-log-search-bench-{Guid.NewGuid():N}.db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE RaskLog (
                    Id INTEGER PRIMARY KEY, Timestamp TEXT NOT NULL, Level INTEGER NOT NULL, Category TEXT NOT NULL,
                    EventId INTEGER NOT NULL, Message TEXT NOT NULL, Exception TEXT, Scopes TEXT);
                """;
            if (withIndex)
            {
                // The same DDL SqliteLogStore.EnsureSearchIndexAsync writes.
                schema.CommandText += """
                    CREATE VIRTUAL TABLE RaskLogSearch USING fts5(
                        Message, Exception, content='RaskLog', content_rowid='Id', tokenize='trigram');
                    CREATE TRIGGER TR_RaskLog_Search_Insert AFTER INSERT ON RaskLog BEGIN
                        INSERT INTO RaskLogSearch (rowid, Message, Exception) VALUES (new.Id, new.Message, new.Exception);
                    END;
                    """;
            }

            schema.ExecuteNonQuery();
        }

        string[] verbs = ["shipped", "created", "paid", "refunded", "retried", "failed", "cancelled", "packed"];
        var random = new Random(42);

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO RaskLog (Timestamp, Level, Category, EventId, Message, Exception) VALUES ('2026-09-21T10:00:00Z', $l, 'Shop.Orders', 0, $m, $e);";
        var level = insert.Parameters.Add("$l", SqliteType.Integer);
        var message = insert.Parameters.Add("$m", SqliteType.Text);
        var exception = insert.Parameters.Add("$e", SqliteType.Text);
        for (var i = 0; i < Rows; i++)
        {
            var id = random.Next(1_000) == 0 ? Needle : $"ORD-{random.Next():X6}";
            level.Value = random.Next(6);
            message.Value = $"Order {id} {verbs[random.Next(verbs.Length)]} in {random.Next(900) + 100} ms";
            exception.Value = random.Next(20) == 0
                ? (object)$"System.InvalidOperationException: order {id} could not be saved\n   at Shop.Orders.Save()"
                : DBNull.Value;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
        return path;
    }
}
