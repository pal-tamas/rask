using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     The application's database, reachable from anywhere — including from inside an entity — without
///     injecting a <see cref="DbContext" /> or an <see cref="IDbContextFactory{TContext}" />.
/// </summary>
/// <remarks>
///     <para>
///         This is the ambient half of Rask's active-record surface. The other half is the static
///         members every <see cref="Model" /> gains (<c>Product.Add</c>, <c>Product.Where</c>,
///         <c>Product.CreateAsync</c>, …) — see <see cref="ModelSet" />. Both read the same ambient
///         context, so the two styles mix freely inside one method.
///     </para>
///     <para>
///         <b>The context is never long-lived.</b> A Rask session lives as long as the browser holds the
///         socket open, and a <see cref="DbContext" /> is neither thread-safe nor meant to accumulate a
///         session's worth of tracked entities — so the ambient context is scoped to a
///         <see cref="UnitOfWork" />, not to the session. Open one with <see cref="Begin" />:
///     </para>
///     <example>
///         <code>
/// await using var uow = Db.Begin();
///
/// Product.Add(new Product("Anvil"));
/// Order.Remove(stale);
///
/// await uow.SaveChangesAsync();   // one transaction over both
///         </code>
///     </example>
///     <para>
///         Outside a unit of work there is no ambient context to share, so the self-committing members
///         (<c>Product.CreateAsync</c>, <c>entity.SaveAsync()</c>, <c>Product.Where(…).ToListAsync()</c>)
///         each open and dispose one of their own. Inside a unit of work they join it instead and commit
///         with it. That is the whole difference between the two tiers, and it is why the tracker verbs
///         (<c>Add</c>/<c>Remove</c>/<c>Update</c>, which in EF Core do nothing until a save) require a
///         unit of work and say so if none is open.
///     </para>
///     <para>
///         <see cref="Configure(IServiceProvider)" /> supplies the factory. A Rask app never calls it —
///         the host does, from the same <c>IDbContextFactory&lt;T&gt;</c> registration the batteries
///         already read. Everything else needs one line at startup.
///     </para>
/// </remarks>
public static class Db
{
    private static readonly AsyncLocal<UnitOfWork?> AmbientUnitOfWork = new();

    private static Func<DbContext>? _factory;

    /// <summary>
    ///     The unit of work this async flow is running inside, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    ///     Reading this is the supported way to ask "am I already in a transaction?" — for a method that
    ///     wants to join an outer unit of work when there is one and commit on its own when there is not,
    ///     without forcing that choice on its caller.
    /// </remarks>
    public static UnitOfWork? CurrentUnitOfWork => AmbientUnitOfWork.Value;

    /// <summary>Whether a <see cref="UnitOfWork" /> is open on this async flow.</summary>
    public static bool HasCurrent => AmbientUnitOfWork.Value is not null;

    /// <summary>
    ///     The ambient <see cref="DbContext" /> — the escape hatch to everything EF Core can do that this
    ///     surface does not wrap.
    /// </summary>
    /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
    public static DbContext Current =>
        AmbientUnitOfWork.Value?.Context
        ?? throw new InvalidOperationException(
            "There is no ambient DbContext here because no unit of work is open. Wrap the work in " +
            "`await using var uow = Db.Begin();` and commit it with `await uow.SaveChangesAsync();`, or " +
            "use the self-committing members (Product.CreateAsync, entity.SaveAsync(), " +
            "Product.Where(...).ToListAsync()), which open a context of their own.");

    /// <summary>Whether <see cref="Configure(IServiceProvider)" /> (or an overload) has run.</summary>
    public static bool IsConfigured => _factory is not null;

    /// <summary>
    ///     Points the ambient database at the application's <c>DbContext</c>, read off its own
    ///     <see cref="IDbContextFactory{TContext}" /> registration in <paramref name="services" />.
    /// </summary>
    /// <remarks>
    ///     Call once, after the container is built. A Rask app gets this for free from the host; anything
    ///     else calls it beside the rest of its startup:
    ///     <code>
    /// var app = builder.Build();
    /// Db.Configure(app.Services);
    ///     </code>
    ///     The context type is the one named by <c>AddRaskData&lt;TContext&gt;()</c>; without that call
    ///     this throws rather than guessing between two databases.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     No context type was registered by <c>AddRaskData&lt;TContext&gt;()</c>.
    /// </exception>
    public static void Configure(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var binding = services.GetService<AmbientContextBinding>()
                      ?? throw new InvalidOperationException(
                          "No DbContext is bound to the ambient database. Register one with " +
                          "`services.AddRaskData<AppDbContext>()` (the generic overload names the context; " +
                          "the non-generic AddRaskData() only registers the interceptors), or call " +
                          "Db.Configure(IDbContextFactory<AppDbContext>) with the factory directly.");

        _factory = binding.CreateContext;
    }

    /// <summary>Points the ambient database at <paramref name="factory" />.</summary>
    /// <remarks>
    ///     The direct form, for a test or a console app that has a factory in hand and no container worth
    ///     building. <c>Db.Configure(services)</c> is the one an application uses.
    /// </remarks>
    public static void Configure<TContext>(IDbContextFactory<TContext> factory)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.CreateDbContext;
    }

    /// <summary>
    ///     Points the ambient database at <paramref name="createContext" />, which must return a fresh,
    ///     unshared context on every call.
    /// </summary>
    /// <remarks>
    ///     The unit of work owns and disposes whatever this returns, so handing back the same instance
    ///     twice would let one unit of work dispose another's context.
    /// </remarks>
    public static void Configure(Func<DbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _factory = createContext;
    }

    /// <summary>
    ///     Opens a unit of work and makes its context the ambient one for this async flow, or joins the
    ///     one already open.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The returned handle must be disposed — <c>await using</c> — and nothing is written until
    ///         <see cref="UnitOfWork.SaveChangesAsync" /> is called on it. Disposing without saving
    ///         discards the tracked changes, so a unit of work that throws part-way leaves the database
    ///         untouched.
    ///     </para>
    ///     <para>
    ///         <b>Nesting joins rather than nests.</b> Calling this inside an open unit of work returns a
    ///         handle onto the same context, and disposing that handle neither saves nor disposes the
    ///         outer one — so a helper method can open a unit of work unconditionally and still take part
    ///         in its caller's transaction. Only the outermost handle owns the context.
    ///     </para>
    ///     <para>
    ///         The context itself is created lazily, on first use, so wrapping a method that turns out to
    ///         touch no data costs one <see cref="AsyncLocal{T}" /> write.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The ambient database has not been configured.</exception>
    public static UnitOfWork Begin()
    {
        if (AmbientUnitOfWork.Value is { } outer)
        {
            return UnitOfWork.Joining(outer);
        }

        var factory = _factory ?? throw new InvalidOperationException(
            "The ambient database has not been configured. A Rask app gets this from the host; " +
            "elsewhere call `Db.Configure(app.Services)` once after building the container, having " +
            "registered the context with `services.AddRaskData<AppDbContext>()`.");

        var unitOfWork = UnitOfWork.Owning(factory, Exit);
        AmbientUnitOfWork.Value = unitOfWork;
        return unitOfWork;
    }

    /// <summary>Commits the ambient unit of work.</summary>
    /// <remarks>
    ///     Shorthand for <c>Db.CurrentUnitOfWork.SaveChangesAsync(…)</c>, for code far enough from the
    ///     <see cref="Begin" /> that holding the handle would mean threading it through.
    /// </remarks>
    /// <returns>The number of state entries written.</returns>
    /// <exception cref="InvalidOperationException">No unit of work is open on this async flow.</exception>
    public static Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        (AmbientUnitOfWork.Value ?? throw new InvalidOperationException(
            "There is nothing to save because no unit of work is open. Open one with " +
            "`await using var uow = Db.Begin();`."))
        .SaveChangesAsync(cancellationToken);

    /// <summary>The ambient context's <see cref="DbSet{TEntity}" /> for <typeparamref name="TEntity" />.</summary>
    /// <remarks>
    ///     The way to reach an entity's set from inside another entity, where the generated static surface
    ///     of the type you want may not be in scope: <c>Db.Set&lt;Order&gt;().Where(o =&gt; o.ProductId == Id)</c>.
    /// </remarks>
    public static DbSet<TEntity> Set<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TEntity>()
        where TEntity : class => Current.Set<TEntity>();

    /// <summary>The ambient context's change-tracking entry for <paramref name="entity" />.</summary>
    public static EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class => Current.Entry(entity);

    /// <summary>Forgets the configured factory. Test seam.</summary>
    /// <remarks>
    ///     Only a test suite that configures the ambient database differently per class needs this; an
    ///     application configures it once at startup and never unconfigures it.
    /// </remarks>
    public static void Reset()
    {
        _factory = null;
        AmbientUnitOfWork.Value = null;
    }

    // Restores the previous ambient value when the OWNING unit of work is disposed. A joining handle
    // never calls this — it did not push anything.
    private static void Exit(UnitOfWork unitOfWork)
    {
        if (ReferenceEquals(AmbientUnitOfWork.Value, unitOfWork))
        {
            AmbientUnitOfWork.Value = null;
        }
    }
}
