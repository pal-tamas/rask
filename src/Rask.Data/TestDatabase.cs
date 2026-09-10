using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A real database for a test, in one line — so a method on a <see cref="Model" /> that reads or
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
/// await using var database = await TestDatabase.StartAsync(o => o.UseSqlite("Data Source=:memory:"));
///
/// await using var uow = Db.Begin();
/// Order.Add(order);
/// await uow.SaveChangesAsync();
///
/// Assert.True(await order.TryCancelAsync());
///         </code>
///     </example>
///     <para>
///         It builds the generated model (so every <see cref="Model" /> in the test assembly is mapped),
///         creates the schema, wires the auditing and soft-delete interceptors so the conventions behave
///         as they do in production, and points the ambient <see cref="Db" /> at it. Disposing clears the
///         ambient database again, so one test cannot leak its database into the next.
///     </para>
///     <para>
///         <b>Provider-agnostic on purpose.</b> The options callback is yours, so this adds no provider
///         dependency to <c>Rask.Data</c> and a test runs against whichever database the app uses. SQLite
///         over a temporary file is the usual choice; <c>:memory:</c> is faster but lives only as long as
///         its connection, so a test that opens several contexts wants a file.
///     </para>
///     <para>
///         Domain-event publication is <em>not</em> wired: <see cref="DomainEventInterceptor" /> needs a
///         service provider to resolve handlers through, which is more than a database fixture should
///         invent. Assert on <see cref="IHasDomainEvents.DomainEvents" /> instead — it is the model's own
///         record of what happened, and checking it needs no dispatcher at all.
///     </para>
/// </remarks>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly RaskDbContext _schemaOwner;

    private TestDatabase(RaskDbContext schemaOwner) => _schemaOwner = schemaOwner;

    /// <summary>Builds the database, creates its schema, and makes it the ambient one.</summary>
    /// <param name="configure">
    ///     Points EF Core at a provider, e.g. <c>o =&gt; o.UseSqlite($"Data Source={path}")</c>.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock the audit stamps come from. Pass a fake to assert on <c>CreatedAt</c>/<c>UpdatedAt</c>.
    /// </param>
    /// <param name="cancellationToken">Cancels schema creation.</param>
    public static async Task<TestDatabase> StartAsync(
        Action<DbContextOptionsBuilder> configure,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var clock = timeProvider ?? TimeProvider.System;

        RaskDbContext Create()
        {
            var builder = new DbContextOptionsBuilder<RaskDbContext>();
            configure(builder);

            // The same order the container registers them in: soft delete rewrites Deleted -> Modified
            // first, so auditing then stamps and versions the update it produced.
            builder.AddInterceptors(new SoftDeleteInterceptor(clock), new AuditingInterceptor(clock));
            return new RaskDbContext(builder.Options);
        }

        // Held open for the fixture's lifetime, which is what keeps a "Data Source=:memory:" database
        // alive: SQLite drops an in-memory database when its last connection closes, so a fixture that
        // created the schema and disposed would hand the first query an empty file.
        var schemaOwner = Create();
        await schemaOwner.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        Db.Configure(Create);
        return new TestDatabase(schemaOwner);
    }

    /// <summary>The ambient database, for the rare assertion that wants the context itself.</summary>
    public RaskDbContext Context => _schemaOwner;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _schemaOwner.DisposeAsync().ConfigureAwait(false);
    }
}
