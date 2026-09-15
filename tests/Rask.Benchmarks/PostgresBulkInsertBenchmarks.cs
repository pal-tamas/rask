using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Rask.Data;

namespace Rask.Benchmarks;

// #1063: on a client-server database, which write shape should BulkInsertAsync's SkipChangeTracking path take?
// SQLite settled on one prepared single-row INSERT rebound per row (SqliteBulkInsertBenchmarks), because a local
// file has no round trip. A server does, and its cost is the latency to the server times the number of trips:
//
//   ChangeTracker      — BulkInsertAsync's default: AddRange + SaveChanges per batch, which EF batches into
//                        multi-statement round trips (Npgsql's MaxBatchSize, 1,000 by default in EF 10).
//   FastPath           — BulkInsertAsync(SkipChangeTracking) as shipped. It was one round trip per row (failing on
//                        Npgsql; ~20 s for 10,000 rows at 1 ms, as PerRowPrepared shows). After #1063 it packs rows the
//                        way MultiRowValues does: 136 ms and 11.6 MB for 10,000 rows, 15 iterations.
//   PerRowPrepared     — the old shape in raw ADO.NET: one prepared single-row INSERT, one round trip per row.
//   DbBatch            — raw ADO.NET: N single-row prepared INSERTs per round trip in one DbBatch.
//   MultiRowValues     — raw ADO.NET: INSERT … VALUES (…),(…) with RowsPerTrip rows per statement.
//
// Run against a server with realistic RTT: scripts/run-bulk-insert-benchmarks-local.sh starts PostgreSQL 17 with
// `tc netem` delaying its egress, and exports RASK_PG_BENCH_DB. A container on the same machine with no delay
// flatters every per-row arm, which is how #1063's first measurement came out wrong in both directions.
//
// Every arm stamps the audit columns, as SqliteBulkInsertBenchmarks' arms do, so the raw arms carry the same work.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, RuntimeMoniker.Net10_0, warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class PostgresBulkInsertBenchmarks : PostgresBulkInsertArms
{
    /// <summary>A page of seed data, a realistic import.</summary>
    [Params(1_000, 10_000)]
    public override int Rows { get; set; }

    [Benchmark]
    public Task PerRowPrepared() => RunPerRowPrepared();
}

// 100,000 rows one round trip at a time is minutes per iteration at any real latency, so the per-row arm stops at
// 10,000 and the arms that batch go on here.
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, RuntimeMoniker.Net10_0, warmupCount: 1, iterationCount: 3, invocationCount: 1)]
public class PostgresBulkInsertLargeBenchmarks : PostgresBulkInsertArms
{
    [Params(100_000)]
    public override int Rows { get; set; }
}

public abstract class PostgresBulkInsertArms
{
    /// <summary>Rows per round trip for the batching arms — SQL Server's 1,000-row VALUES cap, so both providers can share it.</summary>
    protected const int RowsPerTrip = 1_000;

    private const int EfBatch = 5_000;

    private static readonly string[] Columns =
        ["Id", "Sku", "Name", "Price", "Stock", "Active", "CreatedAt", "UpdatedAt", "Version"];

    private string _connectionString = null!;
    private PgBenchProduct[] _rows = null!;
    private readonly Dictionary<int, string> _multiRowText = new();

    public abstract int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _connectionString = Environment.GetEnvironmentVariable("RASK_PG_BENCH_DB")
            ?? throw new InvalidOperationException(
                "RASK_PG_BENCH_DB is not set. Run scripts/run-bulk-insert-benchmarks-local.sh, which starts the server.");

        using (var context = NewContext())
        {
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
        }

        _rows = new PgBenchProduct[Rows];
        for (var i = 0; i < Rows; i++)
        {
            _rows[i] = PgBenchProduct.Create(i);
        }
    }

    [IterationSetup]
    public void ClearTable()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "TRUNCATE \"Products\";";
        command.ExecuteNonQuery();
    }

    [GlobalCleanup]
    public void Cleanup() => NpgsqlConnection.ClearAllPools();

    [Benchmark(Baseline = true)]
    public async Task ChangeTracker()
    {
        await using var context = NewContext();
        await context.BulkInsertAsync(_rows, o => o.BatchSize = EfBatch);
    }

    [Benchmark]
    public async Task DbBatch()
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var single = InsertText(1);
        for (var offset = 0; offset < _rows.Length; offset += RowsPerTrip)
        {
            var take = Math.Min(RowsPerTrip, _rows.Length - offset);
            await using var batch = connection.CreateBatch();
            batch.Transaction = transaction;
            for (var i = 0; i < take; i++)
            {
                var command = batch.CreateBatchCommand();
                command.CommandText = single;
                var values = ValuesOf(_rows[offset + i], now);
                for (var c = 0; c < values.Length; c++)
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = values[c] });
                }

                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    [Benchmark]
    public async Task MultiRowValues()
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        for (var offset = 0; offset < _rows.Length; offset += RowsPerTrip)
        {
            var take = Math.Min(RowsPerTrip, _rows.Length - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = MultiRowText(take);
            for (var i = 0; i < take; i++)
            {
                foreach (var value in ValuesOf(_rows[offset + i], now))
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = value });
                }
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    [Benchmark]
    public async Task FastPath()
    {
        await using var context = NewContext();
        await context.BulkInsertAsync(_rows, o =>
        {
            o.SkipChangeTracking = true;
            o.BatchSize = EfBatch;
        });
    }

    protected async Task RunPerRowPrepared()
    {
        var now = DateTime.UtcNow;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertText(1);

        var parameters = new NpgsqlParameter[Columns.Length];
        for (var c = 0; c < Columns.Length; c++)
        {
            parameters[c] = command.Parameters.Add(new NpgsqlParameter { Value = DBNull.Value });
        }

        var first = true;
        foreach (var row in _rows)
        {
            var values = ValuesOf(row, now);
            for (var c = 0; c < parameters.Length; c++)
            {
                parameters[c].Value = values[c];
            }

            if (first)
            {
                await command.PrepareAsync();
                first = false;
            }

            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private string MultiRowText(int rows)
    {
        if (!_multiRowText.TryGetValue(rows, out var text))
        {
            _multiRowText[rows] = text = InsertText(rows);
        }

        return text;
    }

    // Positional $n placeholders, Npgsql's native form, so no arm pays for named-parameter rewriting.
    private static string InsertText(int rows)
    {
        var builder = new System.Text.StringBuilder("INSERT INTO \"Products\" (");
        builder.AppendJoin(", ", Columns.Select(c => $"\"{c}\"")).Append(") VALUES ");
        var n = 1;
        for (var r = 0; r < rows; r++)
        {
            builder.Append(r == 0 ? "(" : ", (");
            for (var c = 0; c < Columns.Length; c++)
            {
                builder.Append(c == 0 ? "$" : ", $").Append(n++);
            }

            builder.Append(')');
        }

        return builder.ToString();
    }

    private static object[] ValuesOf(PgBenchProduct row, DateTime now) =>
        [row.Id, row.Sku, row.Name, row.Price, row.Stock, row.Active, now, now, 0];

    private PgBenchContext NewContext() => new(_connectionString);
}

/// <summary>
/// <see cref="BenchProduct"/> for PostgreSQL: an aggregate, so every arm writes the same columns and the Rask arms
/// carry the stamping every entity gets.
/// </summary>
public sealed class PgBenchProduct : Aggregate<Guid>
{
    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public int Stock { get; private set; }

    public bool Active { get; private set; }

    public static PgBenchProduct Create(int i) => new()
    {
        Id = Guid.NewGuid(),
        Sku = $"SKU-{i:D8}",
        Name = $"Product number {i}",
        Price = 9.99m + i,
        Stock = i % 500,
        Active = i % 3 != 0,
    };
}

/// <summary>The PostgreSQL bench context: the same model and auditing interceptor as <see cref="BenchContext"/>.</summary>
public sealed class PgBenchContext(string connectionString) : DbContext
{
    public DbSet<PgBenchProduct> Products => Set<PgBenchProduct>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseNpgsql(connectionString)
            .AddInterceptors(new AuditingInterceptor(TimeProvider.System));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PgBenchProduct>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Sku).IsRequired();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.ApplyRaskConventions();
    }
}
