using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     One provider a conformance suite runs on: how a context reaches it, how its tables are made and dropped, and why
///     its facts are skipped when its server is not there.
/// </summary>
/// <remarks>
///     A suite is written once against this, and each provider is a class of three lines naming its leg — so the bodies
///     the providers are held to cannot drift apart. Every leg opens its context the way an app does (<c>UseRaskSqlite</c>,
///     <c>UseRaskSqlServer</c>, <c>UseRaskPostgres</c>) with the interceptors <c>AddRaskData</c> registers.
/// </remarks>
public abstract class ProviderLeg
{
    private static readonly Lazy<IReadOnlyList<IInterceptor>> RaskInterceptors = new(static () =>
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        using var provider = services.BuildServiceProvider();
        return [.. provider.GetServices<ISaveChangesInterceptor>()];
    });

    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Whether the provider's server is there to run against.</summary>
    public abstract bool Available { get; }

    /// <summary>What a skipped fact says.</summary>
    public abstract string SkipReason { get; }

    /// <summary>A SQLite file, made for the suite and deleted after it.</summary>
    public static ProviderLeg Sqlite(string name) => new SqliteLeg(name);

    /// <summary>A SQL Server database of the suite's own, created and dropped by EF.</summary>
    public static ProviderLeg SqlServer(string database) => new SqlServerLeg(database);

    /// <summary>A schema of the suite's own in the PostgreSQL database every class shares.</summary>
    public static ProviderLeg Postgres(string schema) => new PostgresLeg(schema);

    /// <summary>A new context of <typeparamref name="TContext" /> on this provider.</summary>
    public TContext Open<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext =>
        create(Provider(new DbContextOptionsBuilder<TContext>()).AddInterceptors(RaskInterceptors.Value).Options);

    /// <summary>Drops what an earlier run left and creates the context's tables.</summary>
    public abstract Task Recreate(DbContext db);

    /// <summary>Drops the tables, the database or the file the suite made.</summary>
    public abstract Task Drop(DbContext db);

    private protected abstract DbContextOptionsBuilder<TContext> Provider<TContext>(DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext;

    private sealed class SqliteLeg(string name) : ProviderLeg
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-e2e-{name}-{Guid.NewGuid():N}.db");

        public override bool Available => true;

        public override string SkipReason => "";

        public override Task Recreate(DbContext db) => db.Database.EnsureCreatedAsync(Ct);

        public override Task Drop(DbContext db)
        {
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_path);
            return Task.CompletedTask;
        }

        private protected override DbContextOptionsBuilder<TContext> Provider<TContext>(DbContextOptionsBuilder<TContext> options) =>
            options.UseRaskSqliteAt($"Data Source={_path}");
    }

    private sealed class SqlServerLeg(string database) : ProviderLeg
    {
        public override bool Available => Tests.SqlServer.Available;

        public override string SkipReason => Tests.SqlServer.SkipReason;

        public override async Task Recreate(DbContext db)
        {
            await db.Database.EnsureDeletedAsync(Ct);
            await db.Database.EnsureCreatedAsync(Ct);
        }

        public override Task Drop(DbContext db) => db.Database.EnsureDeletedAsync();

        private protected override DbContextOptionsBuilder<TContext> Provider<TContext>(DbContextOptionsBuilder<TContext> options) =>
            options.UseRaskSqlServerAt(Tests.SqlServer.Database(database));
    }

    private sealed class PostgresLeg(string schema) : ProviderLeg
    {
        public override bool Available => Tests.Postgres.Available;

        public override string SkipReason => Tests.Postgres.SkipReason;

        public override Task Recreate(DbContext db) => Tests.Postgres.ResetSchemaAsync(db, schema);

        public override Task Drop(DbContext db) => Tests.Postgres.DropSchemaAsync(db, schema);

        private protected override DbContextOptionsBuilder<TContext> Provider<TContext>(DbContextOptionsBuilder<TContext> options) =>
            options.UseRaskPostgresAt(Tests.Postgres.Required);
    }
}

/// <summary>
///     A conformance suite: the tables made before each fact and dropped after it, on the leg the provider's class names.
/// </summary>
public abstract class ParityTests<TContext>(ProviderLeg leg) : IAsyncLifetime
    where TContext : DbContext
{
    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected ProviderLeg Leg => leg;

    public async ValueTask InitializeAsync()
    {
        if (leg.Available)
        {
            await using var db = Open();
            await leg.Recreate(db);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (leg.Available)
        {
            await using var db = Open();
            await leg.Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A new context on the leg, as a request of its own would open it.</summary>
    private protected abstract TContext Open();

    /// <summary>Skips the fact when the leg's server is not there.</summary>
    private protected void RequireServer() => Assert.SkipUnless(leg.Available, leg.SkipReason);
}
