using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Where the model surface gets its database: the context factory the application registered.
/// </summary>
/// <remarks>
///     <para>
///         Every read on a model — <c>Product.Where(…)</c>, <c>Product.FindAsync(id)</c>,
///         <c>Product.AsQueryable()</c> — and every generated write — <c>Product.CreateAsync(model)</c>,
///         <c>Product.UpdateAsync(id, model)</c>, <c>Product.DeleteAsync(id)</c> — opens a fresh context from
///         here and disposes it before it returns. Nothing is ambient and nothing stays tracked between
///         calls, which is what makes a model safe to read from a page that lives as long as the browser
///         keeps its socket open.
///     </para>
///     <para>
///         <b>Work that spans several changes is ordinary EF Core.</b> A domain operation such as
///         <c>order.Cancel()</c>, or a transaction over two aggregates, injects the context — or its
///         <see cref="IDbContextFactory{TContext}" /> — and calls <c>SaveChangesAsync</c> itself. There is
///         no Rask-owned unit of work to learn.
///     </para>
///     <para>
///         <see cref="Configure(IServiceProvider)" /> supplies the factory. A Rask app never calls it — the
///         host does, from the same <c>IDbContextFactory&lt;T&gt;</c> registration the batteries already
///         read. Everything else needs one line at startup.
///     </para>
/// </remarks>
public static class Db
{
    private static Func<DbContext>? _factory;

    /// <summary>Whether <see cref="Configure(IServiceProvider)" /> (or an overload) has run.</summary>
    public static bool IsConfigured => _factory is not null;

    /// <summary>
    ///     Points the model surface at the application's <c>DbContext</c>, read off its own
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
                          "No DbContext is bound to the model surface. Register one with " +
                          "`services.AddRaskData<AppDbContext>()` (the generic overload names the context; " +
                          "the non-generic AddRaskData() only registers the interceptors), or call " +
                          "Db.Configure(IDbContextFactory<AppDbContext>) with the factory directly.");

        _factory = binding.CreateContext;
    }

    /// <summary>Points the model surface at <paramref name="factory" />.</summary>
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
    ///     Points the model surface at <paramref name="createContext" />, which must return a fresh,
    ///     unshared context on every call.
    /// </summary>
    /// <remarks>
    ///     Each call owns and disposes whatever this returns, so handing back the same instance twice
    ///     would let one read dispose another's context.
    /// </remarks>
    public static void Configure(Func<DbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _factory = createContext;
    }

    /// <summary>Forgets the configured factory. Test seam.</summary>
    /// <remarks>
    ///     Only a test suite that configures the database differently per class needs this; an
    ///     application configures it once at startup and never unconfigures it.
    /// </remarks>
    public static void Reset() => _factory = null;

    /// <summary>A fresh context the caller owns and disposes.</summary>
    /// <exception cref="InvalidOperationException">The model surface has not been configured.</exception>
    internal static DbContext CreateContext() =>
        (_factory ?? throw new InvalidOperationException(
            "The model database has not been configured. A Rask app gets this from the host; elsewhere " +
            "call `Db.Configure(app.Services)` once after building the container, having registered the " +
            "context with `services.AddRaskData<AppDbContext>()`."))();
}
