using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Benchmarks;

// What reading rows of an aggregate costs per row, on a query that only reads: a queue's poll, a cache lookup,
// a list page. An aggregate that raises nothing must not pay for the events it never raised.
[MemoryDiagnoser]
public class AggregateMaterializationBenchmarks
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<LedgerReadContext> _options = null!;

    [Params(1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<LedgerReadContext>().UseSqlite(_connection).Options;

        using var db = new LedgerReadContext(_options);
        db.Database.EnsureCreated();
        db.AddRange(Enumerable.Range(0, Rows).Select(static i => LedgerRow.Of("row " + i)));
        db.SaveChanges();
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark]
    public int Load()
    {
        using var db = new LedgerReadContext(_options);
        return db.Set<LedgerRow>().AsNoTracking().ToList().Count;
    }
}

public sealed class LedgerRow : Aggregate<long>
{
    private LedgerRow() { }

    public string Note { get; private set; } = "";

    public static LedgerRow Of(string note) => new() { Note = note };
}

public sealed class LedgerReadContext(DbContextOptions<LedgerReadContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerRow>();
        modelBuilder.ApplyRaskConventions();
    }
}
