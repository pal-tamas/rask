using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Data;
using Rask.SQLite;

namespace Rask.Benchmarks;

// Loading many rows at once is the one write shape EF Core deliberately does not answer: EF 7 shipped
// ExecuteUpdate/ExecuteDelete and its own plan says bulk *inserts* are out of scope, so every app that
// seeds, imports or migrates data hand-rolls this. These arms measure what the alternatives actually cost
// against one WAL database, from the naive loop to the raw ceiling:
//
//   PerRowSaveChanges    — the naive loop: one SaveChanges (and so one transaction) per row.
//   AddRangeSaveChanges  — AddRange + one SaveChanges. EF batches statements into one round-trip, but the
//                          change tracker holds every entity and the interceptor walks all of them.
//   TunedEfChunked       — the best you can do inside EF: AutoDetectChanges off, chunked, tracker cleared
//                          per chunk, all under one outer transaction.
//   MultiRowValues       — raw ADO.NET: INSERT ... VALUES (..),(..) packed to SQLite's 32,766-parameter
//                          statement limit, one prepared command per chunk shape.
//   PreparedReuse        — raw ADO.NET: one prepared single-row INSERT, parameters rebound per row.
//
// Every arm stamps the same audit columns a Rask app gets from AuditingInterceptor, so the EF arms carry
// the interceptor and the raw arms stamp by hand — otherwise the raw ceiling would be measured against
// work the EF path is doing and the raw path is not.
//
// RunStrategy.Monitoring with invocationCount=1: each invocation is milliseconds-to-seconds of real I/O
// and needs an empty table, so the per-iteration truncate must not be amortised into the measurement.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, RuntimeMoniker.Net10_0, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class SqliteBulkInsertBenchmarks
{
    private string _dbPath = null!;
    private string _connectionString = null!;
    private BenchProduct[] _rows = null!;

    /// <summary>Row counts: a page of seed data, a realistic import, a big one.</summary>
    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"rask-bulk-bench-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        using (var context = NewContext())
        {
            context.Database.EnsureCreated();
        }

        _rows = new BenchProduct[Rows];
        for (var i = 0; i < Rows; i++)
        {
            _rows[i] = BenchProduct.Create(i);
        }
    }

    [IterationSetup]
    public void ClearTable()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Products;";
        command.ExecuteNonQuery();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task PerRowSaveChanges()
    {
        await using var context = NewContext();
        foreach (var row in _rows)
        {
            context.Products.Add(row);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
    }

    [Benchmark]
    public async Task AddRangeSaveChanges()
    {
        await using var context = NewContext();
        context.Products.AddRange(_rows);
        await context.SaveChangesAsync();
    }

    [Benchmark]
    public async Task TunedEfChunked()
    {
        await using var context = NewContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        await using var transaction = await context.Database.BeginTransactionAsync();
        for (var offset = 0; offset < _rows.Length; offset += EfChunk)
        {
            var take = Math.Min(EfChunk, _rows.Length - offset);
            context.Products.AddRange(_rows.AsSpan(offset, take).ToArray());
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }

        await transaction.CommitAsync();
    }

    [Benchmark]
    public async Task BulkInsert()
    {
        await using var context = NewContext();
        await context.BulkInsertAsync(_rows);
    }

    [Benchmark]
    public async Task BulkInsertSkippingChangeTracking()
    {
        await using var context = NewContext();
        await context.BulkInsertAsync(_rows, o => o.SkipChangeTracking = true);
    }

    [Benchmark]
    public async Task MultiRowValues()
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // SQLite caps a statement at 32,766 bound variables (probed on the bundled 3.53.4), so a row of N
        // columns *could* pack 32766/N — 4,095 rows here. Don't: Microsoft.Data.Sqlite binds by parameter
        // NAME, one sqlite3_bind_parameter_index lookup per parameter, so a statement's binding cost is
        // quadratic in its parameter count. Measured at max packing this arm ran 192 ms / 7.2 s / 2.07 min
        // for 1k / 10k / 100k rows — 10x the rows costing ~37x the time, and at 100k it was 130x slower than
        // simply reusing one prepared single-row INSERT. A modest statement keeps the win without the cliff.
        const int rowsPerStatement = 200;
        await using var transaction = connection.BeginImmediate();

        for (var offset = 0; offset < _rows.Length; offset += rowsPerStatement)
        {
            var take = Math.Min(rowsPerStatement, _rows.Length - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = BuildInsert(take);

            for (var i = 0; i < take; i++)
            {
                Bind(command, _rows[offset + i], i, now);
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    [Benchmark]
    public async Task PreparedReuse()
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginImmediate();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildInsert(1);

        var parameters = new SqliteParameter[Columns.Length];
        for (var c = 0; c < Columns.Length; c++)
        {
            parameters[c] = command.Parameters.Add(
                new SqliteParameter { ParameterName = $"${Columns[c]}0", Value = DBNull.Value });
        }

        command.Prepare();

        foreach (var row in _rows)
        {
            var values = ValuesOf(row, now);
            for (var c = 0; c < parameters.Length; c++)
            {
                parameters[c].Value = values[c];
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    [Benchmark]
    public async Task PreparedReuseSync()
    {
        var now = DateTime.UtcNow;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginImmediate();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildInsert(1);

        var parameters = new SqliteParameter[Columns.Length];
        for (var c = 0; c < Columns.Length; c++)
        {
            parameters[c] = command.Parameters.Add(
                new SqliteParameter { ParameterName = $"${Columns[c]}0", Value = DBNull.Value });
        }

        command.Prepare();

        // Same work as PreparedReuse, executed synchronously. SQLite is a local file with no true async I/O
        // — Microsoft.Data.Sqlite's ExecuteNonQueryAsync runs the same synchronous work on the calling
        // thread — so awaiting once per row buys nothing and costs a state machine each time.
        foreach (var row in _rows)
        {
            var values = ValuesOf(row, now);
            for (var c = 0; c < parameters.Length; c++)
            {
                parameters[c].Value = values[c];
            }

            command.ExecuteNonQuery();
        }

        await transaction.CommitAsync();
    }

    private const int EfChunk = 5_000;

    private static readonly string[] Columns =
        ["Id", "Sku", "Name", "Price", "Stock", "Active", "CreatedAt", "UpdatedAt"];

    private static string BuildInsert(int rows)
    {
        var builder = new System.Text.StringBuilder("INSERT INTO Products (");
        builder.AppendJoin(", ", Columns).Append(") VALUES ");

        for (var r = 0; r < rows; r++)
        {
            if (r > 0)
            {
                builder.Append(", ");
            }

            builder.Append('(');
            for (var c = 0; c < Columns.Length; c++)
            {
                if (c > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('$').Append(Columns[c]).Append(r);
            }

            builder.Append(')');
        }

        return builder.Append(';').ToString();
    }

    private static object[] ValuesOf(BenchProduct row, DateTime now) =>
        [row.Id.ToString(), row.Sku, row.Name, row.Price, row.Stock, row.Active, now, now];

    private static void Bind(SqliteCommand command, BenchProduct row, int index, DateTime now)
    {
        var values = ValuesOf(row, now);
        for (var c = 0; c < Columns.Length; c++)
        {
            command.Parameters.AddWithValue($"${Columns[c]}{index}", values[c]);
        }
    }

    private BenchContext NewContext() => new(_connectionString);
}

/// <summary>A representative row: a few scalars of each storage class, plus Rask's audit stamps.</summary>
public sealed class BenchProduct : Entity<Guid>
{
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public int Stock { get; private set; }

    public bool Active { get; private set; }

    public static BenchProduct Create(int i) => new()
    {
        Id = Guid.NewGuid(),
        Sku = $"SKU-{i:D8}",
        Name = $"Product number {i}",
        Price = 9.99m + i,
        Stock = i % 500,
        Active = i % 3 != 0,
    };
}

/// <summary>The bench context: production pragmas, and the auditing interceptor a Rask app carries.</summary>
public sealed class BenchContext(string connectionString) : DbContext
{
    private readonly string _connectionString = connectionString;

    public DbSet<BenchProduct> Products => Set<BenchProduct>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseRaskSqlite(_connectionString)
            .AddInterceptors(new AuditingInterceptor(TimeProvider.System));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BenchProduct>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).IsRequired();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.ApplyRaskConventions();
    }
}
