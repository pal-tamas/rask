using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Rask.SQLite;

namespace Rask.Benchmarks;

// SQLite has no decimal type, so EF Core stores one as TEXT and sorts it with a collating sequence
// (`ORDER BY "x" COLLATE EF_DECIMAL`) — a managed callback invoked O(n log n) times per sort. The
// alternative is to model the amount as an integer count of minor units in an INTEGER column, which
// sorts with SQLite's own native comparison.
//
// docs/data-access.md tells people to reach for integer minor units on a large, frequently sorted table.
// These arms are what that advice is measured against, so it can be stated as a number rather than a
// hunch:
//
//   DecimalCollated  — ORDER BY over the TEXT column through the collation. What EF emits.
//   DecimalBinary    — the same column with SQLite's built-in byte comparison. WRONG (it orders "10.00"
//                      before "9.50"), included only to separate the collation's cost from the sort's.
//   IntegerNative    — ORDER BY over an INTEGER minor-units column. The native ceiling.
//   IntegerIndexed   — the same, served by an index, which is the case a TEXT column cannot match
//                      unless the index itself carries the collation.
//
// RunStrategy.Monitoring with invocationCount=1: each invocation is a full table sort of real I/O, and
// amortising it into a tight loop would measure the page cache rather than the sort.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, RuntimeMoniker.Net10_0, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class SqliteDecimalOrderingBenchmarks
{
    private string _dbPath = null!;
    private SqliteConnection _connection = null!;

    /// <summary>Row counts: a page of data, a real table, a big one.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rask-decimal-order-bench-{Guid.NewGuid():N}.db");

        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        SqlitePragmas.Apply(_connection, new SqlitePragmaOptions());
        SqliteCollations.Apply(_connection);

        Execute("CREATE TABLE prices (text_amount TEXT NOT NULL, minor_units INTEGER NOT NULL)");

        // Values spread across magnitudes so the sort is real work and a byte comparison genuinely
        // disagrees with a numeric one.
        using (var transaction = _connection.BeginTransaction())
        {
            using var insert = _connection.CreateCommand();
            insert.CommandText = "INSERT INTO prices (text_amount, minor_units) VALUES ($t, $m)";
            var text = insert.Parameters.Add("$t", SqliteType.Text);
            var minor = insert.Parameters.Add("$m", SqliteType.Integer);

            var random = new Random(20260826);
            for (var i = 0; i < Rows; i++)
            {
                var minorUnits = random.NextInt64(1, 100_000_00);
                text.Value = (minorUnits / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                minor.Value = minorUnits;
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        Execute("CREATE INDEX ix_prices_minor ON prices(minor_units)");

        Execute("ANALYZE");
    }

    [Benchmark(Baseline = true)]
    public int DecimalCollated() => Consume($"SELECT text_amount FROM prices ORDER BY text_amount COLLATE {SqliteCollations.Decimal}");

    [Benchmark]
    public int DecimalBinary() => Consume("SELECT text_amount FROM prices ORDER BY text_amount");

    [Benchmark]
    public int IntegerNative() => Consume("SELECT minor_units FROM prices ORDER BY minor_units + 0");

    [Benchmark]
    public int IntegerIndexed() => Consume("SELECT minor_units FROM prices ORDER BY minor_units");

    private int Consume(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        return rows;
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }
}
