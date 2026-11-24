using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Rask.SQLite;

namespace Rask.Data;

/// <summary>
///     <c>TestDatabase.Start()</c> with nothing to configure: a SQLite database in memory, of the test's own, through
///     <c>UseRaskSqlite</c> — so it compares decimals and text as the app's database does.
/// </summary>
/// <remarks>
///     <para>
///         Each database is a named, shared-cache one in memory (<c>Data Source=rask-test-…;Mode=Memory;Cache=Shared</c>),
///         kept alive by one connection the fixture holds open. Every context it hands out — <c>Context</c>,
///         <c>Factory</c>, the model surface's reads and writes (<c>Order.Where(…)</c>), <c>Load&lt;T&gt;</c> — opens a
///         connection of its own to the same name, so they all see the same rows. Disposing the fixture closes the
///         last connection, and the database is gone.
///     </para>
///     <para>
///         Every test gets a name of its own, so tests run side by side: each sees its own database through the model
///         surface too, as long as it starts its database itself (<c>await using var database = await
///         TestDatabase.Start&lt;ShopContext&gt;();</c> in the test). One started in a class fixture is the
///         process-wide one, and its tests take turns.
///     </para>
///     <para>
///         The schema is the model's, created as <c>EnsureCreated</c> creates it, so an app whose migrations are
///         SQL Server's still gets its tables — once per model: the first database over a model creates it, and every
///         later one is copied from that empty schema through SQLite's backup API, so a test's start costs well under a
///         millisecond of schema however many tables the model has. <c>UseRaskSqlite</c> registers what the model asks for on every connection:
///         an invariant decimal order, and the text collation <c>OrderTextBy</c> names.
///     </para>
/// </remarks>
public static class SqliteTestDatabase
{
    extension(TestDatabase)
    {
        /// <summary>Starts a plain <see cref="RaskDbContext" /> over a SQLite database in memory, of the test's own.</summary>
        /// <param name="timeProvider">The clock the audit stamps come from.</param>
        /// <param name="services">Registers what the app's handlers need beyond what Rask registers.</param>
        /// <param name="cancellationToken">Cancels schema creation.</param>
        /// <returns>The fixture, its schema created and the model surface pointed at it.</returns>
        [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
        public static Task<TestDatabase> Start(
            TimeProvider? timeProvider = null,
            Action<IServiceCollection>? services = null,
            CancellationToken cancellationToken = default)
        {
            var memory = InMemory.Open();
            return TestDatabase.StartHolding(memory.Use, memory, timeProvider, services, cancellationToken, memory.CreateSchema);
        }

        /// <summary>Starts the app's own context over a SQLite database in memory, of the test's own.</summary>
        /// <typeparam name="TContext">The app's context, built through its public constructor taking its options.</typeparam>
        /// <param name="timeProvider">The clock the audit stamps come from.</param>
        /// <param name="services">Registers what the app's handlers need beyond what Rask registers.</param>
        /// <param name="cancellationToken">Cancels schema creation.</param>
        /// <returns>The fixture, its schema created and the model surface pointed at it.</returns>
        /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> has no constructor taking its options.</exception>
        [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
        public static Task<TestDatabase<TContext>> Start<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
            TimeProvider? timeProvider = null,
            Action<IServiceCollection>? services = null,
            CancellationToken cancellationToken = default)
            where TContext : RaskDbContext
        {
            var memory = InMemory.Open();
            return TestDatabase.StartHolding<TContext>(memory.Use, memory, timeProvider, services, cancellationToken, memory.CreateSchema);
        }
    }

    /// <summary>One database in memory, alive while its keeper connection is open.</summary>
    private sealed class InMemory(SqliteConnection keeper) : IAsyncDisposable
    {
        private static readonly SqliteOptions Settings = Validated();

        public static InMemory Open()
        {
            // Unpooled: a pooled connection stays open in the pool after its context closes it, and would keep every
            // test's database alive for the rest of the run.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = string.Create(CultureInfo.InvariantCulture, $"rask-test-{Guid.NewGuid():N}"),
                Mode = SqliteOpenMode.Memory,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            }.ToString();
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return new InMemory(connection);
        }

        public void Use(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder builder) =>
            RaskSqliteDbContextOptionsExtensions.UseRaskSqlite(builder, keeper.ConnectionString, Settings);

        /// <summary>The model's schema, copied from its template (<see cref="SqliteSchemaTemplate" />).</summary>
        public Task CreateSchema(Microsoft.EntityFrameworkCore.DbContext context, CancellationToken cancellationToken) =>
            SqliteSchemaTemplate.CreateSchema(context, keeper, cancellationToken);

        public ValueTask DisposeAsync() => keeper.DisposeAsync();

        private static SqliteOptions Validated()
        {
            var options = new SqliteOptions();
            RaskSqliteDbContextOptionsExtensions.Validate(options);
            return options;
        }
    }
}
