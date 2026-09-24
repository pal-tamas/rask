using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Where the model surface gets its database: the context factory the application registered.
/// </summary>
/// <remarks>
///     <para>
///         Every read on a model — <c>Product.Where(…)</c>, <c>Product.FindAsync(id)</c>,
///         <c>Product.AsQueryable()</c> — opens a fresh context from here and disposes it before it
///         returns. Nothing is ambient and nothing stays tracked between
///         calls, which is what makes a model safe to read from a page that lives as long as the browser
///         keeps its socket open.
///     </para>
///     <para>
///         <b>The writes on a model use it the same way.</b> <c>Product.CreateAsync(model)</c>,
///         <c>Product.UpdateAsync(id, model)</c> and <c>Product.DeleteAsync(id)</c> open a context here, save
///         and dispose it — unless they are handed a context, which they save through and leave open. A
///         transaction over two aggregates is that context's <c>Database.BeginTransactionAsync</c>; there is
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
    private static readonly AsyncLocal<IServiceProvider?> AmbientScope = new();
    private static Func<DbContext>? _factory;
    private static int _generation;

    /// <summary>Whether <see cref="Configure(IServiceProvider)" /> (or an overload) has run.</summary>
    public static bool IsConfigured => _factory is not null;

    /// <summary>
    ///     How many times the model surface has been pointed at a database. Part of the read context's
    ///     model cache key — see <see cref="RaskReadDbContext" />.
    /// </summary>
    internal static int Generation => Volatile.Read(ref _generation);

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
        Interlocked.Increment(ref _generation);

        // Both halves from one call. Forgetting the read side would not fail here — it would fail at the
        // first Product.Read in some page, which is the worst place to learn about a line of startup you
        // did not write. ReadDb resolves its factory lazily, so an app with no read context registered pays
        // nothing and still gets a message that names what to register.
        ReadDb.Configure(services);
    }

    /// <summary>Points the model surface at <paramref name="factory" />.</summary>
    /// <remarks>
    ///     The direct form, for a test or a console app that has a factory in hand and no container worth
    ///     building. <c>Db.Configure(services)</c> is the one an application uses.
    /// </remarks>
    public static void Configure<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(IDbContextFactory<TContext> factory)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory.CreateDbContext;
        Interlocked.Increment(ref _generation);
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
        Interlocked.Increment(ref _generation);
    }

    /// <summary>Forgets the configured factory, on both halves. Test seam.</summary>
    /// <remarks>
    ///     Only a test suite that configures the database differently per class needs this; an
    ///     application configures it once at startup and never unconfigures it.
    /// </remarks>
    public static void Reset()
    {
        _factory = null;
        Interlocked.Increment(ref _generation);
        ReadDb.Reset();
    }

    /// <summary>
    ///     Builds every context from <paramref name="scope" /> until the returned scope is disposed.
    /// </summary>
    /// <param name="scope">The service scope to resolve the context factory from.</param>
    /// <returns>A scope that restores the previous binding.</returns>
    /// <remarks>
    ///     <para>
    ///         A Rask read is a static call — <c>Product.Read.Where(…)</c> — so it runs outside any DI scope
    ///         and normally builds its context from the factory <see cref="Configure(IServiceProvider)" />
    ///         captured once at startup. That is the right default and stays the fallback.
    ///     </para>
    ///     <para>
    ///         It is not enough when the context needs something <b>scoped</b> to answer correctly — the
    ///         signed-in principal, and through it the tenant. The host opens this around the work of a live
    ///         session and around every HTTP request, whose <c>IServiceScope</c> already holds the
    ///         <see cref="IPrincipalSource" /> for that user, and every read inside then builds its context from
    ///         the same scope the user belongs to — and <see cref="Current" /> answers from it.
    ///     </para>
    ///     <para>
    ///         Additive on purpose: with no ambient scope open, nothing about how a context is built changes.
    ///     </para>
    /// </remarks>
    public static IDisposable UseScope(IServiceProvider scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new ScopeBinding(scope);
    }

    /// <summary>The scope <see cref="UseScope" /> opened — the live session's — or null outside one.</summary>
    internal static IServiceProvider? ScopeServices => AmbientScope.Value;

    /// <summary>
    ///     The signed-in principal, read from the scope <see cref="UseScope" /> opened, or null.
    /// </summary>
    /// <remarks>
    ///     Resolved per call rather than captured, because the scope is the session's and the principal in it
    ///     changes — a sign-in, a sign-out, an admin switching tenant.
    /// </remarks>
    internal static ClaimsPrincipal? PrincipalFromScope() =>
        AmbientScope.Value?.GetService<IPrincipalSource>()?.Current;

    /// <summary>A fresh context the caller owns and disposes.</summary>
    /// <exception cref="InvalidOperationException">The model surface has not been configured.</exception>
    internal static DbContext CreateContext()
    {
        // The ambient scope wins when one is open, because it is the only thing that can build a context
        // carrying the current user — and therefore the current tenant.
        if (AmbientScope.Value is { } scope &&
            scope.GetService<AmbientContextBinding>() is { } scoped)
        {
            return scoped.CreateContext();
        }

        return (_factory ?? throw new InvalidOperationException(
            "The model database has not been configured. A Rask app gets this from the host; elsewhere " +
            "call `Db.Configure(app.Services)` once after building the container, having registered the " +
            "context with `services.AddRaskData<AppDbContext>()`."))();
    }

    private sealed class ScopeBinding : IDisposable
    {
        private readonly IServiceProvider? _previous;
        private readonly Ambient.ServicesScope _ambient;
        private bool _disposed;

        internal ScopeBinding(IServiceProvider scope)
        {
            _previous = AmbientScope.Value;
            AmbientScope.Value = scope;

            // The same scope, for the batteries' static calls (`Cache.Remember`, `Jobs.Enqueue`).
            _ambient = Ambient.Enter(scope);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _ambient.Dispose();
                AmbientScope.Value = _previous;
            }
        }
    }
}
