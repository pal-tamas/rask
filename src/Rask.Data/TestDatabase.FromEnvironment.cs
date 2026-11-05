using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

public partial class TestDatabase
{
    /// <summary>
    ///     The database an environment variable's connection string names, over the app's own context — or, with the
    ///     variable unset, the test SKIPPED rather than failed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For the tests that need the app's real database server rather than a SQLite file: a machine with the
    ///         variable set runs them, and one without it (a laptop with no server, a pull-request build) reports them
    ///         skipped with a reason that names the variable.
    ///     </para>
    ///     <code>
    /// await using var database = await TestDatabase.FromEnvironment&lt;ShopContext&gt;("SHOP_TEST_DB", (o, cs) =&gt; o.UseSqlServer(cs));
    ///
    /// await using var context = await database.Factory.CreateDbContextAsync();
    ///     </code>
    ///     <para>
    ///         By default each test gets a database of its own — the named one with a unique suffix, created and
    ///         migrated before the test and dropped after it. <c>o =&gt; o.Shared = true</c> runs against the named
    ///         database itself, one test at a time across every process on the machine (see
    ///         <see cref="TestDatabaseOptions" />).
    ///     </para>
    ///     <para>
    ///         Either way one environment database is open at a time in a process — <see cref="Db" /> is process-wide —
    ///         so a second one waits for the first to be disposed. The skip is xUnit v3's dynamic skip; under another
    ///         framework the test fails with the same reason.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TContext">The app's context, built through its public constructor taking its options.</typeparam>
    /// <param name="variable">The environment variable holding the connection string, e.g. <c>"SHOP_TEST_DB"</c>.</param>
    /// <param name="configure">
    ///     Points EF Core at the provider with the connection string it is given — <c>(o, cs) =&gt; o.UseSqlServer(cs)</c>.
    ///     Per test, that is the test's own database's connection string, not the variable's.
    /// </param>
    /// <param name="options">One database per test (the default), or the named one shared.</param>
    /// <param name="cancellationToken">Cancels waiting for a turn and bringing the schema up.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> has no constructor taking its options.</exception>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    public static async Task<TestDatabase<TContext>> FromEnvironment<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        string variable,
        Action<DbContextOptionsBuilder, string> configure,
        Action<TestDatabaseOptions>? options = null,
        CancellationToken cancellationToken = default)
        where TContext : RaskDbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);
        ArgumentNullException.ThrowIfNull(configure);

        var named = Environment.GetEnvironmentVariable(variable) ?? "";
        if (SkipWithout(variable, named) is { } skip)
        {
            throw skip;
        }

        var settings = new TestDatabaseOptions();
        options?.Invoke(settings);
        var construct = AppContextConstructor.For<TContext>();

        var turn = await TestDatabaseTurn.Take(settings.Shared ? settings.LockFile ?? DefaultLockFile(named) : null, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var connectionString = settings.Shared ? named : await OwnDatabase(named, configure, construct).ConfigureAwait(false);
            void Configure(DbContextOptionsBuilder builder) => configure(builder, connectionString);

            if (!settings.Shared)
            {
                turn.DropOnEnd(() => Drop(Configure, construct));
            }

            return await Open(Configure, construct, timeProvider: null, SchemaStep.Migrate, turn, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await turn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     What ends the test as skipped when <paramref name="variable" /> holds no connection string — built here and
    ///     thrown by the caller, since xUnit takes a skip token as thrown, even one a test catches.
    /// </summary>
    internal static InvalidOperationException? SkipWithout(string variable, string connectionString) =>
        string.IsNullOrWhiteSpace(connectionString)
            ? TestSkip.Because($"{variable} is not set, so this test has no database to run on. Set it to a connection string to run it.")
            : null;

    /// <summary>The connection string of the test's own database, named for the provider the app configured.</summary>
    private static async Task<string> OwnDatabase<TContext>(
        string named, Action<DbContextOptionsBuilder, string> configure, Func<DbContextOptions<TContext>, TContext> construct)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        configure(builder, named);
        var probe = construct(builder.Options);
        await using (probe.ConfigureAwait(false))
        {
            return PerTestConnectionString.For(named, probe.Database.ProviderName, Guid.NewGuid().ToString("N"));
        }
    }

    private static async Task Drop<TContext>(Action<DbContextOptionsBuilder> configure, Func<DbContextOptions<TContext>, TContext> construct)
        where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>();
        configure(builder);
        var context = construct(builder.Options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.EnsureDeletedAsync().ConfigureAwait(false);
        }
    }

    /// <summary>One lock per database, so two suites on one standing copy take turns however they name it.</summary>
    private static string DefaultLockFile(string connectionString)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
        return Path.Combine(Path.GetTempPath(), $"rask-test-db-{hash[..16]}.lock");
    }
}
