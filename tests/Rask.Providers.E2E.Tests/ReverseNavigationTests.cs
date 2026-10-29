using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant's silo, and the gauges fitted to it.</summary>
public sealed class Silo : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private readonly List<Gauge> _gauges = [];

    private Silo() { }

    public string Name { get; private set; } = "";

    public string Region { get; private set; } = "";

    public int Capacity { get; private set; }

    public IReadOnlyList<Gauge> Gauges => _gauges;

    public static Silo Built(string id, string region, int capacity, params string[] gauges)
    {
        var silo = new Silo { Id = id, Name = id, Region = region, Capacity = capacity };
        silo._gauges.AddRange(gauges.Select(Gauge.Labelled));
        return silo;
    }

    public static Silo Fitted(string id, string gauge, string reading)
    {
        var silo = new Silo { Id = id, Name = id, Region = "north" };
        silo._gauges.Add(Gauge.Reading(gauge, reading));
        return silo;
    }
}

public sealed class Gauge : Entity<string>
{
    private Gauge() { }

    public string Label { get; private set; } = "";

    public string? ReadingId { get; private set; }

    internal static Gauge Labelled(string id) => new() { Id = id, Label = id };

    internal static Gauge Reading(string id, string reading) => new() { Id = id, Label = id, ReadingId = reading };
}

/// <summary>Grain moved between two silos, read off one gauge: ids only.</summary>
public sealed class Transfer : Aggregate<string>, IFormModel, ISoftDelete
{
    private Transfer() { }

    public string FromSiloId { get; private set; } = "";

    public string ToSiloId { get; private set; } = "";

    public string? GaugeId { get; private set; }

    public int Litres { get; private set; }

    public decimal Cost { get; private set; }

    public static Transfer Moved(string id, string from, string to, int litres, string? gauge = null) =>
        new() { Id = id, FromSiloId = from, ToSiloId = to, Litres = litres, Cost = litres * 0.5m, GaugeId = gauge };
}

/// <summary>A grade every tenant shares.</summary>
public sealed class Grade : Aggregate<string>, IFormModel, IDeletable
{
    private Grade() { }

    public static Grade Named(string id) => new() { Id = id };
}

/// <summary>A tenant's reading of a shared grade.</summary>
public sealed class Reading : Aggregate<string>, IFormModel, IDeletable, IPerTenant
{
    private Reading() { }

    public string GradeId { get; private set; } = "";

    public int Value { get; private set; }

    public static Reading Of(string id, string grade, int value) => new() { Id = id, GradeId = grade, Value = value };
}

/// <summary>Only these tables, so each provider creates exactly what the scenario reads.</summary>
public sealed class ReverseDbContext(DbContextOptions<ReverseDbContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "reverse_nav";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Silo>(b =>
        {
            b.ToTable("Silo", Schema);
            b.Property(s => s.Id).HasMaxLength(64);
        });
        modelBuilder.Entity<Gauge>(b =>
        {
            b.ToTable("Gauge", Schema);
            b.Property(g => g.Id).HasMaxLength(64);
            b.Property(g => g.ReadingId).HasMaxLength(64);
        });
        modelBuilder.Entity<Transfer>(b =>
        {
            b.ToTable("Transfer", Schema);
            b.Property(t => t.Id).HasMaxLength(64);
            b.Property(t => t.FromSiloId).HasMaxLength(64);
            b.Property(t => t.ToSiloId).HasMaxLength(64);
            b.Property(t => t.GaugeId).HasMaxLength(64);
            b.Property(t => t.Cost).HasPrecision(18, 2);
        });
        modelBuilder.Entity<Grade>(b =>
        {
            b.ToTable("Grade", Schema);
            b.Property(g => g.Id).HasMaxLength(64);
        });
        modelBuilder.Entity<Reading>(b =>
        {
            b.ToTable("Reading", Schema);
            b.Property(r => r.Id).HasMaxLength(64);
            b.Property(r => r.GradeId).HasMaxLength(64);
        });

        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>A read face's reverse collections on SQL Server: each query one statement, each tenant only its rows.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerReverseNavigationTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_reverse_nav";

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var app = App(new ReverseNavigationScenario.Log());
        await using var db = await ReverseNavigationScenario.OpenAsync(app);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var app = App(new ReverseNavigationScenario.Log());
            await using var db = await ReverseNavigationScenario.OpenAsync(app);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Every_reverse_collection_query_is_one_statement_with_the_right_rows()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var log = new ReverseNavigationScenario.Log();
        await using var app = App(log);

        await ReverseNavigationScenario.RunAsync(app, log);
    }

    private static ServiceProvider App(ReverseNavigationScenario.Log log) =>
        ReverseNavigationScenario.Build(o => o.UseSqlServer(SqlServer.Database(DatabaseName)), log);
}

/// <summary>The same on PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresReverseNavigationTests
{
    [Fact]
    public async Task Every_reverse_collection_query_is_one_statement_with_the_right_rows()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        var log = new ReverseNavigationScenario.Log();
        await using var app = ReverseNavigationScenario.Build(o => o.UseRaskPostgresAt(Postgres.Required), log);
        await using (var db = await ReverseNavigationScenario.OpenAsync(app))
        {
            await Postgres.ResetSchemaAsync(db, ReverseDbContext.Schema);
        }

        await ReverseNavigationScenario.RunAsync(app, log);
    }
}

/// <summary>Wired the way an application wires it, and the reads both providers answer.</summary>
internal static class ReverseNavigationScenario
{
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid Globex = Guid.NewGuid();

    internal static ServiceProvider Build(Action<DbContextOptionsBuilder> provider, Log log)
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<ReverseDbContext>();
        services.AddDbContextFactory<ReverseDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        services.AddDbContextFactory<RaskReadDbContext>(o =>
        {
            provider(o);
            o.AddInterceptors(log);
        });
        return services.BuildServiceProvider();
    }

    internal static Task<ReverseDbContext> OpenAsync(ServiceProvider app) =>
        app.GetRequiredService<IDbContextFactory<ReverseDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);

    internal static async Task RunAsync(ServiceProvider app, Log log)
    {
        Db.Configure(app);
        try
        {
            await SeedAsync();

            using (Tenant.Use(Acme))
            {
                Assert.Equal(["s3", "s2"], await Once(log, () => Silo
                    .Where(s => !s.TransfersFrom.Any())
                    .OrderBy(s => s.Capacity + s.TransfersTo.Sum(t => t.Litres))
                    .Select(s => s.Name)));

                Assert.Equal(
                    ["s1 2 0 17.50", "s2 0 1 0.00", "s3 0 1 0.00"],
                    (await Once(log, () => Silo.OrderBy(s => s.Name).Select(s => new
                    {
                        s.Name,
                        Out = s.TransfersFrom.Count,
                        In = s.TransfersTo.Count(),
                        Cost = s.TransfersFrom.Sum(t => t.Cost),
                    }))).Select(r => FormattableString.Invariant($"{r.Name} {r.Out} {r.In} {r.Cost:0.00}")));

                Assert.Equal(["north:1", "south:1"], await Once(log, () => Silo.Query((q, ct) => q
                    .Where(s => s.TransfersTo.Any())
                    .GroupBy(s => s.Region)
                    .Select(g => new { Region = g.Key, Silos = g.Count() })
                    .OrderBy(g => g.Region)
                    .Select(g => g.Region + ":" + g.Silos)
                    .ToListAsync(ct), TestContext.Current.CancellationToken)));

                Assert.Equal(["g1"], await Once(log, () => Gauge.Where(g => g.Transfers.Any()).Select(g => g.Label)));
                Assert.Equal(["g1", "g2"], (await Gauge.Select(g => g.Label)).Order(StringComparer.Ordinal));
                Assert.Equal(2, (await Grade.Select(g => g.Readings.Sum(r => r.Value))).Single());
            }

            using (Tenant.Use(Globex))
            {
                Assert.Equal(100, (await Grade.Select(g => g.Readings.Sum(r => r.Value))).Single());
                Assert.Equal(["gx"], await Gauge.Select(g => g.Label));
                Assert.Equal(1, await Silo.Query((q, ct) => q.SelectMany(s => s.TransfersFrom).CountAsync(ct), TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            Db.Reset();
        }
    }

    // Acme: s1 -> s2 (30 l, through g1), s1 -> s3 (5 l), and s3 -> s2 soft-deleted. Globex: x1 -> x1, and silo x2 whose
    // gauge gx names Acme's reading a1, which never makes it Acme's. Grade 95 is read as 2 by Acme and as 100 by Globex.
    private static async Task SeedAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await Grade.Named("95").Save(cancellationToken: token);
        using (Tenant.Use(Acme))
        {
            await Silo.Built("s1", "north", 100, "g1", "g2").Save(cancellationToken: token);
            await Silo.Built("s2", "south", 50).Save(cancellationToken: token);
            await Silo.Built("s3", "north", 10).Save(cancellationToken: token);
            await Reading.Of("a1", "95", 2).Save(cancellationToken: token);
        }

        using (Tenant.Use(Globex))
        {
            await Silo.Built("x1", "north", 1).Save(cancellationToken: token);
            await Silo.Fitted("x2", "gx", "a1").Save(cancellationToken: token);
            await Reading.Of("g1", "95", 100).Save(cancellationToken: token);
        }

        await Transfer.Moved("t1", "s1", "s2", 30, gauge: "g1").Save(cancellationToken: token);
        await Transfer.Moved("t2", "s1", "s3", 5).Save(cancellationToken: token);
        await Transfer.Moved("t3", "s3", "s2", 1000).Save(cancellationToken: token);
        await Transfer.Moved("x", "x1", "x1", 7).Save(cancellationToken: token);
        await Transfer.Delete("t3", cancellationToken: token);
    }

    private static async Task<T> Once<T>(Log log, Func<Task<T>> read)
    {
        log.Clear();
        var result = await read();
        Assert.Single(log.Texts);
        return result;
    }

    private static Task<List<T>> Once<T>(Log log, Func<Projection<SiloRead, T>> read) => Once(log, async () => await read());

    private static Task<List<T>> Once<T>(Log log, Func<Projection<GaugeRead, T>> read) => Once(log, async () => await read());

    /// <summary>Every query the read context sends.</summary>
    internal sealed class Log : DbCommandInterceptor
    {
        private readonly List<string> _texts = [];

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (_texts)
                {
                    return [.. _texts];
                }
            }
        }

        public void Clear()
        {
            lock (_texts)
            {
                _texts.Clear();
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            lock (_texts)
            {
                _texts.Add(command.CommandText);
            }

            return ValueTask.FromResult(result);
        }
    }
}
