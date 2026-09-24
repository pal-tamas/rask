using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
/// Where the read faces get their database: the same one the aggregates are written to, opened through a
/// context of their own.
/// </summary>
/// <remarks>
/// <para>
/// The read faces cannot live in the write context. Two entity types may not map one table unless they are a
/// table-splitting pair, and a read face is deliberately not related to its aggregate — so they get their own
/// context, which holds no aggregates and therefore has nothing to track, change or save.
/// </para>
/// <para>
/// It is registered from the same configuration the write context is, so there is one database to configure
/// and one place that says where it is. An app that points its own context somewhere else entirely can say so
/// with <see cref="Configure(Func{RaskReadDbContext})" />.
/// </para>
/// </remarks>
public static class ReadDb
{
    private static Func<DbContext>? _factory;

    /// <summary>Whether the read side has been pointed at a database.</summary>
    public static bool IsConfigured => _factory is not null;

    /// <summary>Points the read faces at the read context the host registered.</summary>
    /// <remarks>
    ///     <para>
    ///         Called once after the container is built, beside <see cref="Db.Configure(IServiceProvider)" />.
    ///     </para>
    ///     <para>
    ///         <b>The factory is resolved on first use, not here.</b> Resolving an
    ///         <see cref="IDbContextFactory{TContext}" /> builds its options, which is where a provider reads
    ///         the connection string — so resolving it at startup would fail the BOOT of an app that has no
    ///         database configured and never reads one, and it would fail from a line the app did not write.
    ///         Deferring it is also what <see cref="Db" /> does, through its own binding.
    ///     </para>
    /// </remarks>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "IDbContextFactory<RaskReadDbContext> keeps the context's constructors, which carry EF Core's "
                        + "own [RequiresUnreferencedCode]. Nothing is constructed here unless the app registered that "
                        + "factory, and the registration (AddDbContextFactory<RaskReadDbContext>) already reports EF's "
                        + "IL2026 to the app — the same warning, at the line that chose EF.")]
    public static void Configure(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _factory = () =>
            (services.GetService<IDbContextFactory<RaskReadDbContext>>()
             ?? throw new InvalidOperationException(
                 "No read context is registered, so the generated read faces have nothing to open. "
                 + "A Rask host registers it beside the application's own context; elsewhere call "
                 + "services.AddDbContextFactory<RaskReadDbContext>((sp, o) => o.UseRaskDatabase(sp))."))
            .CreateDbContext();
    }

    /// <summary>Points the read faces at <paramref name="createContext" />, which returns a fresh context each call.</summary>
    /// <remarks>The direct form, for a test, or for an app whose read side is a different database entirely.</remarks>
    public static void Configure(Func<RaskReadDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _factory = createContext;
    }

    /// <summary>Forgets the configured factory. Test seam, like <see cref="Db.Reset" />.</summary>
    public static void Reset() => _factory = null;

    /// <summary>
    ///     A fresh context for whichever side <typeparamref name="TEntity" /> belongs to.
    /// </summary>
    /// <remarks>
    ///     The one place the two halves meet. <see cref="ModelQuery{TEntity}" /> and the queryable behind
    ///     <c>AsQueryable()</c> are the same machinery for both, so they ask here rather than each knowing
    ///     which database to open: a read face opens the read context, an aggregate the write one.
    /// </remarks>
    internal static DbContext OpenFor<TEntity>() => Side<TEntity>.IsRead ? CreateContext() : Db.CreateContext();

    /// <summary>A fresh read context the caller owns and disposes.</summary>
    /// <exception cref="InvalidOperationException">The read side has not been configured.</exception>
    internal static DbContext CreateContext() =>
        (_factory ?? throw new InvalidOperationException(
            "The read faces have not been pointed at a database. A Rask app gets this from the host, beside "
            + "Db.Configure(app.Services); elsewhere call ReadDb.Configure(app.Services) once after building "
            + "the container."))();

    // Asked once per closed generic rather than per call: the answer cannot change, and an interface
    // check on every terminal operator would be paid by every read in the app.
    private static class Side<TEntity>
    {
        internal static readonly bool IsRead = typeof(IReadModel).IsAssignableFrom(typeof(TEntity));
    }
}
