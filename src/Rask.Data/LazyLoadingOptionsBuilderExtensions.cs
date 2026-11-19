using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data;

/// <summary>The one way back to lazy loading, which a context Rask configures has switched off.</summary>
public static class LazyLoadingOptionsBuilderExtensions
{
    /// <summary>
    ///     Lets this context load a navigation the moment it is read — EF Core's lazy loading, by proxies
    ///     (<c>UseLazyLoadingProxies</c>) or an injected <c>ILazyLoader</c>. Off unless asked, and discouraged.
    /// </summary>
    /// <remarks>
    ///     A navigation read in a loop is a query per row (N+1) that no line of the code shows, and a component's render,
    ///     a form's validation or a JSON serializer reads navigations without anybody meaning to load them. Say what a read
    ///     needs instead — <c>Include</c>, a projection, a read face. With lazy loading off, an unloaded navigation reads
    ///     as EF Core leaves it (null, or an empty collection) and runs no query.
    ///     <code>
    ///     services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o
    ///         .UseRaskSqlite(sp)
    ///         .UseLazyLoadingProxies()
    ///         .AllowLazyLoading()   // without it, RASK126, and nothing loads
    ///         .AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;()));
    ///     </code>
    /// </remarks>
    public static DbContextOptionsBuilder AllowLazyLoading(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (optionsBuilder.Options.FindExtension<LazyLoadingAllowedExtension>() is null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new LazyLoadingAllowedExtension());
        }

        return optionsBuilder;
    }

    /// <inheritdoc cref="AllowLazyLoading(DbContextOptionsBuilder)" />
    public static DbContextOptionsBuilder<TContext> AllowLazyLoading<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        AllowLazyLoading((DbContextOptionsBuilder)optionsBuilder);
        return optionsBuilder;
    }
}
