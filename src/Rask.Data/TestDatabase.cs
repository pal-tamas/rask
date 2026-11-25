using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     A real database for a test, in one line — so a method on a <see cref="Aggregate{TId}" /> that reads or
///     writes can be tested as directly as one that only changes fields.
/// </summary>
/// <remarks>
///     <para>
///         Model behaviour splits in two, and only the second half needs this. A method that just changes
///         the model's own state — <c>order.Cancel()</c> setting a status and raising an event — is a
///         plain object with plain fields, so it needs no database, no fixture and no mock. A method that
///         asks the database something needs a database, and this is the cheapest honest one:
///     </para>
///     <example>
///         <code>
/// await using var database = await TestDatabase.Start();   // SQLite in memory, the test's own
///
/// database.Context.Add(Order.Place("A-1"));
/// await database.Context.SaveChangesAsync();
///
/// Assert.Equal(1, await Order.Count());
///         </code>
///     </example>
///     <para>
///         It builds the generated model (so every <see cref="Aggregate{TId}" /> in the test assembly is mapped),
///         creates the schema, wires the interceptors an app's context has so the conventions behave
///         as they do in production, and points both <see cref="Db" /> and <see cref="ReadDb" /> at it —
///         so <c>Order.Where(…)</c> queries the same rows the write side just saved. Disposing clears them
///         again, so one test cannot leak its database into the next.
///     </para>
///     <para>
///         <b>Provider-agnostic on purpose.</b> The options callback is yours, so this adds no provider
///         dependency to <c>Rask.Data</c>. With nothing to configure — <c>TestDatabase.Start&lt;AppDbContext&gt;()</c>,
///         from <c>Rask.SQLite.EntityFrameworkCore</c> — it is a SQLite database in memory of the test's own, which every
///         context it hands out shares; that is the usual choice.
///     </para>
///     <para>
///         <b>A save runs what it runs in the app.</b> The interceptors come from services registered as the host
///         registers them — Rask.Data's whole chain, in its order, and the outbox's when the model maps its table — so
///         a saved aggregate's domain events are published after the commit to the same <c>IEventHandler</c>s, and a
///         durable handler's event is stored in the save's own transaction (or recorded by <c>Outbox.Fake()</c>). A
///         handler is resolved from those services, and a context it opens is this database's.
///     </para>
///     <para>
///         <b>The batteries are their fakes.</b> When the model maps a battery's table — a RaskApp's context maps them all —
///         that battery is its test fake for the database's lifetime: <c>Mail.Send</c>, <c>Jobs.Enqueue</c>,
///         <c>Cache.Remember</c>, <c>Push.Send</c> and <c>Files.Save</c> in a save handler record what they would have done
///         instead of throwing for want of a host, and the test reads them back: <c>database.Mail.Sent().Once()</c>,
///         <c>await database.Jobs.Run()</c>. A fake the test made before starting the database (<c>using var mail = Mail.Fake();</c>)
///         is the one used; a battery the test registers itself through <c>services</c> replaces the fake.
///     </para>
/// </remarks>
/// <seealso cref="TestDatabase{TContext}" />
public partial class TestDatabase : IAsyncDisposable
{
    private readonly RaskDbContext _schemaOwner;
    private readonly Func<RaskReadDbContext> _openRead;
    private readonly Func<DbContext> _openWrite;
    private readonly Action<DbContextOptionsBuilder> _configure;
    private readonly IAsyncDisposable? _lease;
    private readonly Db.FlowDatabase _flow;
    private readonly TestDatabaseFakes _fakes;

    // Binds the model surface to this database for the flow that is starting it — so it must run in that flow, outside
    // any async method of its own (see Db.BindFlow).
    private protected TestDatabase(
        RaskDbContext schemaOwner,
        Func<RaskReadDbContext> openRead,
        Func<DbContext> openWrite,
        Action<DbContextOptionsBuilder> configure,
        IServiceProvider services,
        IAsyncDisposable? lease,
        TestDatabaseFakes fakes)
    {
        _fakes = fakes;
        _schemaOwner = schemaOwner;
        _openRead = openRead;
        _openWrite = openWrite;
        _configure = configure;
        _lease = lease;
        _flow = Db.BindFlow(openWrite, openRead, services);
    }

    /// <summary>Builds the database over a plain <see cref="RaskDbContext" />, creates its schema, and makes it the ambient one.</summary>
    /// <remarks>
    ///     For an app with a context of its own, <see cref="Start{TContext}" /> runs the test against that one instead.
    /// </remarks>
    /// <param name="configure">
    ///     Points EF Core at a provider, e.g. <c>o =&gt; o.UseSqlite($"Data Source={path}")</c>.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock the audit stamps come from. Pass a fake to assert on <c>CreatedAt</c>/<c>UpdatedAt</c>.
    /// </param>
    /// <param name="services">
    ///     Registers what the app's handlers need beyond what Rask registers — a fake mailer, a recorder — into the
    ///     services a save publishes its domain events through.
    /// </param>
    /// <param name="cancellationToken">Cancels schema creation; none passed, the work in progress's — inside a test, the test's.</param>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    public static Task<TestDatabase> Start(
        Action<DbContextOptionsBuilder> configure,
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? services = null,
        CancellationToken cancellationToken = default) =>
        StartHolding(configure, lease: null, timeProvider, services, cancellationToken);

    /// <summary>
    ///     Builds the database over the app's own context, creates its schema, and makes it the ambient one — so the
    ///     test runs against exactly the model and the <c>SaveChangesAsync</c> the app runs in production.
    /// </summary>
    /// <remarks>
    ///     <typeparamref name="TContext" /> is built the way the app builds it: through its public constructor taking
    ///     <c>DbContextOptions&lt;TContext&gt;</c> (or <c>DbContextOptions</c>). Its <c>OnModelCreating</c> additions —
    ///     a battery's tables, its own entities — are in the schema, and its <c>SaveChangesAsync</c> override runs on
    ///     every save, whether through <see cref="TestDatabase{TContext}.Context" /> or through an aggregate.
    /// </remarks>
    /// <typeparam name="TContext">The app's context.</typeparam>
    /// <param name="configure">
    ///     Points EF Core at a provider, e.g. <c>o =&gt; o.UseSqlServer(connectionString)</c>.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock the audit stamps come from. Pass a fake to assert on <c>CreatedAt</c>/<c>UpdatedAt</c>.
    /// </param>
    /// <param name="services">
    ///     Registers what the app's handlers need beyond what Rask registers — a fake mailer, a recorder — into the
    ///     services a save publishes its domain events through.
    /// </param>
    /// <param name="cancellationToken">Cancels schema creation; none passed, the work in progress's — inside a test, the test's.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> has no constructor taking its options.</exception>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    public static Task<TestDatabase<TContext>> Start<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        Action<DbContextOptionsBuilder> configure,
        TimeProvider? timeProvider = null,
        Action<IServiceCollection>? services = null,
        CancellationToken cancellationToken = default)
        where TContext : RaskDbContext
    {
        ArgumentNullException.ThrowIfNull(configure);
        return StartHolding<TContext>(configure, lease: null, timeProvider, services, cancellationToken);
    }

    /// <summary>
    ///     <see cref="Start(Action{DbContextOptionsBuilder}, TimeProvider?, Action{IServiceCollection}?, CancellationToken)" />, holding
    ///     <paramref name="lease" /> — what keeps the database alive, an in-memory one's open connection — until disposed.
    ///     Its schema made by <c>createSchema</c>, or by <c>EnsureCreated</c> when that is null.
    /// </summary>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    internal static Task<TestDatabase> StartHolding(
        Action<DbContextOptionsBuilder> configure,
        IAsyncDisposable? lease,
        TimeProvider? timeProvider,
        Action<IServiceCollection>? services,
        CancellationToken cancellationToken,
        Func<DbContext, CancellationToken, Task>? createSchema = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Opened here, in the caller's flow, and only awaited inside: see Open. No token passed: the work in progress's —
        // inside a test, the test's own (Rask.Testing's hook).
        return Started(Open<RaskDbContext>(
            configure, static options => new RaskDbContext(options), timeProvider, services, createSchema ?? SchemaStep.Create, lease,
            Ambient.Or(cancellationToken)));

        static async Task<TestDatabase> Started(Task<TestDatabase<RaskDbContext>> opening) =>
            await opening.ConfigureAwait(false);
    }

    /// <summary>
    ///     <see cref="Start{TContext}(Action{DbContextOptionsBuilder}, TimeProvider?, Action{IServiceCollection}?, CancellationToken)" />, holding
    ///     <paramref name="lease" /> until disposed.
    ///     Its schema made by <c>createSchema</c>, or by <c>EnsureCreated</c> when that is null.
    /// </summary>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    internal static Task<TestDatabase<TContext>> StartHolding<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        Action<DbContextOptionsBuilder> configure,
        IAsyncDisposable? lease,
        TimeProvider? timeProvider,
        Action<IServiceCollection>? services,
        CancellationToken cancellationToken,
        Func<DbContext, CancellationToken, Task>? createSchema = null)
        where TContext : RaskDbContext
    {
        ArgumentNullException.ThrowIfNull(configure);
        // No token passed: the work in progress's — inside a test, the test's own (Rask.Testing's hook).
        return Open(
            configure, AppContextConstructor.For<TContext>(), timeProvider, services, createSchema ?? SchemaStep.Create, lease,
            Ambient.Or(cancellationToken));
    }

    // Not async: everything up to the schema runs in the caller's flow, so the database it binds the model surface to is
    // the caller's own (Db.BindFlow) — and tests side by side each read and write their own.
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    private static Task<TestDatabase<TContext>> Open<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        Action<DbContextOptionsBuilder> configure,
        Func<DbContextOptions<TContext>, TContext> construct,
        TimeProvider? timeProvider,
        Action<IServiceCollection>? addServices,
        Func<TContext, CancellationToken, Task> createSchema,
        IAsyncDisposable? lease,
        CancellationToken cancellationToken)
        where TContext : RaskDbContext
    {
        ArgumentNullException.ThrowIfNull(configure);

        var clock = timeProvider ?? Clock.TimeProvider;

        // The app's own interceptors, from services registered as the app registers them (TestDatabaseServices) — filled
        // in below, once the model they depend on is known, and before the first context is opened.
        // Last in the chain: keeps the fixture's own context as the database has it, once the others shaped the save.
        IInterceptor[] interceptors = [];
        var fakes = new TestDatabaseFakes(); // filled in the caller's flow, so each fake takes its battery's place for the test
        var keeper = new FixtureContextKeeper();

        TContext Create()
        {
            var builder = OptionsLayout.LikeAddDbContext<TContext>();
            configure(builder);

            // Said up front, as a Rask host says it: options the context's OnConfiguring would change get EF Core's
            // internal services built twice, and a test run that builds more than twenty sets is refused.
            RaskDbContext.WithSharedWarnings(builder);
            builder.AddInterceptors(interceptors);
            return construct(builder.Options);
        }

        // One delegate each, handed to every place that opens a context, so disposing can tell whether the process-wide
        // binding is still this database's before it clears it.
        Func<TContext> write = Create;
        Func<RaskReadDbContext> read = ReadOpener(configure);

        ServiceProvider Wired(IModel? model)
        {
            var wired = TestDatabaseServices.For(clock, model, write, read, addServices, fakes);
            interceptors = [.. TestDatabaseServices.Interceptors(wired), keeper];
            return wired;
        }

        // Rask's own services first, so the context that builds the model is configured as every later one is (EF Core
        // keys its internal services on the interceptors that shape materialization). Building the model loads a
        // battery's assembly when the model maps its tables; with the batteries announced, the services are registered
        // again with what they add — the outbox's interceptor.
        var services = Wired(model: null);
        var model = ModelOf(write);
        if (!TestDatabaseBatteries.All.IsEmpty)
        {
            services.Dispose();
            services = Wired(model);
        }

        // Held open for the fixture's lifetime, which is what keeps a "Data Source=:memory:" database
        // alive: SQLite drops an in-memory database when its last connection closes, so a fixture that
        // created the schema and disposed would hand the first query an empty file.
        var schemaOwner = keeper.Keep(write());
        Db.Configure(write);
        ReadDb.Configure(read);
        return new TestDatabase<TContext>(schemaOwner, read, write, configure, services, Holding(services, lease), fakes)
            .WithSchema(createSchema, cancellationToken);
    }

    // The read side is the same database through a context of its own, so `Order.Where(…)` works in a test exactly as it
    // does in an app — and it is configured AFTER Db, because building the read model mirrors the write model and so needs
    // the write side already pointed somewhere.
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    private static Func<RaskReadDbContext> ReadOpener(Action<DbContextOptionsBuilder> configure) =>
        () =>
        {
            var builder = OptionsLayout.LikeAddDbContext<RaskReadDbContext>();
            configure(builder);
            RaskReadDbContext.Prepare(builder);
            return new RaskReadDbContext(builder.Options);
        };

    // The model the app's context builds. EF Core keeps it, so the contexts opened afterwards build none.
    private static IModel ModelOf<TContext>(Func<TContext> open)
        where TContext : DbContext
    {
        using var probe = open();
        return probe.Model;
    }

    // What the database lets go of when it is disposed: the services its saves published through, then the lease.
    private static IAsyncDisposable Holding(ServiceProvider services, IAsyncDisposable? lease) =>
        lease is null ? services : new Both(services, lease);

    private sealed class FlowBinding : IDisposable
    {
        private readonly Db.FlowDatabase _bound;
        private readonly Db.FlowDatabase? _previous;

        public FlowBinding(Db.FlowDatabase bound)
        {
            _bound = bound;
            _previous = Db.EnterFlow(bound);
        }

        public void Dispose() => Db.RestoreFlow(_bound, _previous);
    }

    private sealed class Both(IAsyncDisposable first, IAsyncDisposable second) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await first.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                await second.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>The fixture's own context — the way to seed rows and to run a domain operation under test.</summary>
    /// <remarks>
    ///     <para>
    ///         One long-lived, tracking context for the fixture's lifetime, so an entity added through it stays
    ///         tracked here. The model surface (<c>Product.Where(…)</c>, <c>Product.Create(model)</c>) opens contexts of
    ///         its own and sees only what was saved.
    ///     </para>
    ///     <para>
    ///         It reads what the database has, as a page's context opened for its request would: a row another context
    ///         of this database saved is the saved row here too (an unchanged tracked instance takes the saved values,
    ///         its version included; a deleted one is let go), while a row the test changed and has not saved stays the
    ///         test's. A save through it that fails lets go of everything it tracks, so a refused change does not ride
    ///         along with the next save.
    ///     </para>
    /// </remarks>
    public RaskDbContext Context => _schemaOwner;

    /// <summary>The batteries' fakes this database stands in with — what <c>database.Mail</c>, <c>database.Jobs</c> read.</summary>
    internal TestDatabaseFakes Fakes => _fakes;

    /// <summary>Loads one aggregate whole, by key, from a context of its own.</summary>
    /// <remarks>
    ///     <para>
    ///         What a test needs after a write: the row as the DATABASE has it, not the instance
    ///         <see cref="Context" /> is still tracking. Its children come with it, and global query filters
    ///         apply — a soft-deleted root is not found.
    ///     </para>
    ///     <para>
    ///         <b>Deliberately a fixture member and not an aggregate one.</b> An application does not need
    ///         this: it shows rows with <c>Product.Where(…)</c>, fills a form with <c>Product.Model(id)</c>
    ///         and changes one with <c>Product.Update(id, …)</c> or a context it saves. Asserting that a
    ///         save really happened is a test's need, so it lives on the test fixture.
    ///     </para>
    /// </remarks>
    /// <param name="key">The aggregate's primary key.</param>
    /// <param name="cancellationToken">Cancels the load; none passed, the work in progress's — inside a test, the test's.</param>
    public Task<TEntity?> Load<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(object key, CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);
        return FindByKeyAsync<TEntity>(_openWrite, [key], Ambient.Or(cancellationToken));
    }

    /// <summary>Loads one aggregate whole, by composite key.</summary>
    /// <param name="keyValues">The key's values, in the order the key declares them.</param>
    /// <param name="cancellationToken">Cancels the load; none passed, the work in progress's — inside a test, the test's.</param>
    public Task<TEntity?> Load<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(object?[] keyValues, CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        return FindByKeyAsync<TEntity>(_openWrite, keyValues, Ambient.Or(cancellationToken));
    }

    /// <summary>A fresh read context, for asserting on the read model itself.</summary>
    /// <remarks>
    ///     <c>Order.Where(…)</c> opens its own and disposes it, so a test only needs this to look at what the
    ///     read side was MAPPED to — a column name, a navigation, an empty change tracker.
    /// </remarks>
    public RaskReadDbContext OpenRead() => _openRead();

    /// <summary>
    ///     Options for another context type over this same database — a second context the app has beside its own, a
    ///     host's context with interceptors of its own: <c>new LegacyDb(database.Options&lt;LegacyDb&gt;().Options)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The provider is configured exactly as the fixture's own contexts are — through <c>UseRaskSqlite</c> on the
    ///         same in-memory database for <c>TestDatabase.Start&lt;T&gt;()</c>, or the <c>configure</c> callback the
    ///         test passed — so text, decimals and moments compare as they do in the app, and every row either context
    ///         saves the other reads.
    ///     </para>
    ///     <para>
    ///         Only the provider: none of the fixture's interceptors are added, so the context saves exactly as its own
    ///         <c>OnConfiguring</c> and whatever <c>AddInterceptors(…)</c> the caller adds say. The schema is the
    ///         fixture's model's; a table only <typeparamref name="TOther" /> maps is the caller's to create.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TOther">The other context.</typeparam>
    /// <returns>A builder the caller may add to, then hand its <c>Options</c> to the context's constructor.</returns>
    public DbContextOptionsBuilder<TOther> Options<TOther>()
        where TOther : DbContext
    {
        var builder = OptionsLayout.LikeAddDbContext<TOther>();
        _configure(builder);
        return builder;
    }

    /// <summary>
    ///     Points the model surface at this database for the calling test, until the returned binding is disposed — for a
    ///     database a class or collection fixture started, which its tests would otherwise share through the process.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A database started in the test itself needs none of this: it is bound to the test's flow already. One started
    ///         in a fixture's <c>InitializeAsync</c> is not — that ran in a flow of the test framework's own, which is gone
    ///         by the time a test runs — so <c>Order.Where(…)</c> in a test reaches it only through the process-wide
    ///         binding, which the last database started anywhere holds. Rask.Testing's xUnit v3 hook calls this before each
    ///         test of a class whose fixture is, or holds, a database; elsewhere a test binds it on its first line —
    ///         <c>using var bound = fixture.Database.Bind();</c> — and reads and writes it beside fixtures of other classes
    ///         running in parallel.
    ///     </para>
    ///     <para>Not async, so it binds the flow of whatever calls it; disposing puts back what was bound before.</para>
    /// </remarks>
    /// <returns>The binding; dispose it to let go of it.</returns>
    public IDisposable Bind() => new FlowBinding(_flow);

    /// <inheritdoc />
    /// <remarks>Not async up to the unbinding, so the flow that disposes it — the test's — forgets it too.</remarks>
    public ValueTask DisposeAsync()
    {
        Db.UnbindFlow(_flow);
        _fakes.Release();
        // Only while the process-wide binding is still this database's: another test's started since is that test's.
        Db.ResetIf(_openWrite);
        ReadDb.ResetIf(_openRead);
        GC.SuppressFinalize(this);
        return LetGo();
    }

    /// <summary>Creates the schema through the fixture's own context; on a failure, lets the database go again.</summary>
    private protected async Task CreateSchema<TContext>(
        TContext schemaOwner, Func<TContext, CancellationToken, Task> createSchema, CancellationToken cancellationToken)
        where TContext : RaskDbContext
    {
        try
        {
            await createSchema(schemaOwner, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask LetGo()
    {
        await _schemaOwner.DisposeAsync().ConfigureAwait(false);
        if (_lease is not null)
        {
            await _lease.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<TEntity?> FindByKeyAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        Func<DbContext> openContext, object?[] keyValues, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        var context = openContext();
        await using var contextScope = context.ConfigureAwait(false);
        return await AggregateLoad.FindAsync<TEntity>(context, keyValues, cancellationToken).ConfigureAwait(false);
    }
}
