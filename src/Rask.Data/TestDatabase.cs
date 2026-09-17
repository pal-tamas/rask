using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
/// await using var database = await TestDatabase.StartAsync(o => o.UseSqlite("Data Source=:memory:"));
///
/// database.Context.Add(Order.Place("A-1"));
/// await database.Context.SaveChangesAsync();
///
/// Assert.Equal(1, await Order.CountAsync());
///         </code>
///     </example>
///     <para>
///         It builds the generated model (so every <see cref="Aggregate{TId}" /> in the test assembly is mapped),
///         creates the schema, wires the auditing and soft-delete interceptors so the conventions behave
///         as they do in production, and points both <see cref="Db" /> and <see cref="ReadDb" /> at it —
///         so <c>Order.Read</c> queries the same rows the write side just saved. Disposing clears them
///         again, so one test cannot leak its database into the next.
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
    private static readonly FieldInfo BoxedValue =
        typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    private readonly RaskDbContext _schemaOwner;
    private readonly Func<RaskReadDbContext> _openRead;
    private readonly Func<DbContext> _openWrite;

    private TestDatabase(RaskDbContext schemaOwner, Func<RaskReadDbContext> openRead, Func<DbContext> openWrite)
    {
        _schemaOwner = schemaOwner;
        _openRead = openRead;
        _openWrite = openWrite;
    }

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

        // The read side is the same database through a context of its own, so `Order.Read` works in a test
        // exactly as it does in an app — and it is configured AFTER Db, because building the read model
        // mirrors the write model and so needs the write side already pointed somewhere.
        RaskReadDbContext CreateRead()
        {
            var builder = new DbContextOptionsBuilder<RaskReadDbContext>();
            configure(builder);
            return new RaskReadDbContext(builder.Options);
        }

        ReadDb.Configure(CreateRead);
        return new TestDatabase(schemaOwner, CreateRead, Create);
    }

    /// <summary>The fixture's own context — the way to seed rows and to run a domain operation under test.</summary>
    /// <remarks>
    ///     One long-lived, tracking context for the fixture's lifetime, so an entity added through it stays
    ///     tracked here. The model surface (<c>Product.Where(…)</c>, <c>Product.FindAsync(id)</c>) opens contexts of
    ///     its own and sees only what was saved.
    /// </remarks>
    public RaskDbContext Context => _schemaOwner;

    /// <summary>Loads one aggregate whole, by key, from a context of its own.</summary>
    /// <remarks>
    ///     <para>
    ///         What a test needs after a write: the row as the DATABASE has it, not the instance
    ///         <see cref="Context" /> is still tracking. Its children come with it, and global query filters
    ///         apply — a soft-deleted root is not found.
    ///     </para>
    ///     <para>
    ///         <b>Deliberately a fixture member and not an aggregate one.</b> An application does not need
    ///         this: it shows rows with <c>Product.Read</c>, fills a form with <c>Product.ModelAsync(id)</c>
    ///         and changes one with <c>Product.UpdateAsync(id, …)</c> or a context it saves. Asserting that a
    ///         save really happened is a test's need, so it lives on the test fixture.
    ///     </para>
    /// </remarks>
    /// <param name="key">The aggregate's primary key.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public Task<TEntity?> LoadAsync<TEntity>(object key, CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(key);
        return FindByKeyAsync<TEntity>(_openWrite, [key], cancellationToken);
    }

    /// <summary>Loads one aggregate whole, by composite key.</summary>
    /// <param name="keyValues">The key's values, in the order the key declares them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public Task<TEntity?> LoadAsync<TEntity>(object?[] keyValues, CancellationToken cancellationToken = default)
        where TEntity : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        return FindByKeyAsync<TEntity>(_openWrite, keyValues, cancellationToken);
    }

    /// <summary>A fresh read context, for asserting on the read model itself.</summary>
    /// <remarks>
    ///     <c>Order.Read</c> opens its own and disposes it, so a test only needs this to look at what the
    ///     read side was MAPPED to — a column name, a navigation, an empty change tracker.
    /// </remarks>
    public RaskReadDbContext OpenRead() => _openRead();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        ReadDb.Reset();
        await _schemaOwner.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<TEntity?> FindByKeyAsync<TEntity>(
        Func<DbContext> openContext, object?[] keyValues, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
        await using var context = openContext();

        var primaryKey = context.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()
                         ?? throw new InvalidOperationException(
                             $"'{typeof(TEntity).Name}' is not mapped with a primary key by the configured " +
                             "context, so there is nothing to find it by.");

        if (primaryKey.Properties.Count != keyValues.Length)
        {
            throw new ArgumentException(
                $"'{typeof(TEntity).Name}' has a key of {primaryKey.Properties.Count} value(s), but " +
                $"{keyValues.Length} were given.",
                nameof(keyValues));
        }

        if (KeyPredicate<TEntity>(primaryKey, keyValues) is { } predicate)
        {
            // Loading ONE root by its key loads the aggregate whole — its children come with it. A query
            // (Product.Where(…)) deliberately does not: listing a thousand roots should not drag in every
            // line each of them holds. See docs/data.md.
            return await context.Set<TEntity>()
                .AsNoTracking()
                .WithChildren(context)
                .FirstOrDefaultAsync(predicate, cancellationToken)
                .ConfigureAwait(false);
        }

        // A key part with no CLR property (a shadow key) or a type with no equality operator cannot be
        // expressed as a predicate here; EF Core's own Find can. The context is disposed on return, so the
        // row it tracks is released with it.
        var found = await context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);

        if (found is not null)
        {
            await AggregateChildren
                .LoadChildrenAsync(context, found, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return found;
    }

    // row => row.K1 == @k1 && row.K2 == @k2. The values are read through a StrongBox rather than inlined
    // as constants, so EF Core sees parameters: one cached query plan for every key, not one per value.
    private static Expression<Func<TEntity, bool>>? KeyPredicate<TEntity>(IKey primaryKey, object?[] keyValues)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        Expression? body = null;

        for (var i = 0; i < keyValues.Length; i++)
        {
            var property = primaryKey.Properties[i];
            var value = keyValues[i]
                        ?? throw new ArgumentException(
                            $"The key value at position {i} is null, and '{typeof(TEntity).Name}.{property.Name}' " +
                            "is part of the primary key.",
                            nameof(keyValues));

            if (!property.ClrType.IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"The key value at position {i} is a {value.GetType().Name}, but " +
                    $"'{typeof(TEntity).Name}.{property.Name}' is a {property.ClrType.Name}.",
                    nameof(keyValues));
            }

            if (property.PropertyInfo is not { } clrProperty)
            {
                return null;
            }

            var parameter = Expression.Convert(
                Expression.Field(Expression.Constant(new StrongBox<object?>(value)), BoxedValue),
                property.ClrType);

            BinaryExpression equal;
            try
            {
                equal = Expression.Equal(Expression.Property(row, clrProperty), parameter);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        return body is null ? null : Expression.Lambda<Func<TEntity, bool>>(body, row);
    }
}
