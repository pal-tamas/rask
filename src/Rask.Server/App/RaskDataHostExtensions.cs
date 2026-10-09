using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Cqrs;
using Rask.Data;
using Rask.Querying;

namespace Rask.Server;

/// <summary>
///     Rask.Data for a host wired by hand — <c>AddRask()</c> and <c>MapRask&lt;TApp&gt;()</c> rather than
///     <c>RaskApp</c> — in two calls.
/// </summary>
/// <remarks>
///     <code>
/// builder.Services.AddRask();
/// builder.Services.AddRaskData&lt;DomainContext&gt;(o =&gt; o.UseSqlServer(connectionString));
///
/// var app = builder.Build();
/// app.UseAuthentication();
/// app.UseRaskData();
/// app.MapRask&lt;App&gt;();
///     </code>
///     <para>
///         A <c>RaskApp</c> does all of this itself and calls neither. What it does that these do NOT: it
///         chooses the database from <c>Rask:Database</c>, and it applies pending migrations as it starts.
///         Here the database is whatever the callback says, and nothing creates, migrates or checks a table —
///         which is what an app whose schema is owned elsewhere needs.
///     </para>
/// </remarks>
public static class RaskDataHostExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TContext" /> as the context the model surface opens, with everything a
    ///     Rask page and an HTTP endpoint need to read and write through it.
    /// </summary>
    /// <typeparam name="TContext">The application's context — derive it from <see cref="RaskDbContext" />.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the provider and the connection — <c>o =&gt; o.UseSqlServer(…)</c>.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    ///     <para>What it registers, each of which a hand-wired host otherwise writes or goes without:</para>
    ///     <list type="bullet">
    ///         <item>the context factory for <typeparamref name="TContext" />, with Rask's interceptors on it — the tenant stamp, the audit columns, the domain events;</item>
    ///         <item>the factory for the read context the generated read faces are queried through, on the same database;</item>
    ///         <item>who is signed in, for an HTTP request (<c>HttpContext.User</c>) and for a live session (the user that session belongs to) — <c>Current.UserId</c>, <c>Current.Principal</c>;</item>
    ///         <item>the scope a live session's work runs in, so a page's reads and writes have a tenant — where an <c>AddRaskTenant</c> resolver is asked as the session opens;</item>
    ///         <item>the mediator and the query cache, so a save refreshes the session's queries about what it wrote.</item>
    ///     </list>
    ///     <para>
    ///         <paramref name="configure" /> runs for both contexts, so it says where the data is once. It must
    ///         not add Rask's interceptors itself.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddRaskData<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddRaskData<TContext>((_, options) => configure(options));
    }

    /// <summary>
    ///     Registers <typeparamref name="TContext" /> as <see cref="AddRaskData{TContext}(IServiceCollection, Action{DbContextOptionsBuilder})" />
    ///     does, with the application's services at hand for choosing the provider.
    /// </summary>
    /// <typeparam name="TContext">The application's context — derive it from <see cref="RaskDbContext" />.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Chooses the provider and the connection, given the application's services.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRaskData<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddRaskCqrs();
        services.AddRaskQuery();

        DataHostSeams.Add(services);
        services.AddRaskData<TContext>();

        // This host does not create the schema, so an index the model declares may not be in the database. Every
        // declared unique rule is therefore asked as a query before a save, and refuses it the same way.
        services.AddDeclaredRuleChecks();

        services.AddDbContextFactory<TContext>((sp, options) =>
        {
            configure(sp, options);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });

        // Through a factory of its own rather than AddDbContextFactory, as a RaskApp does: EF's tooling lists
        // every DbContextOptions<T> in the container, and this context has no schema to be asked about.
        services.TryAddSingleton<IDbContextFactory<RaskReadDbContext>>(sp => new ReadContextFactory(sp, configure));

        return services;
    }

    /// <summary>
    ///     Makes each HTTP request's services ambient for the data layer, and points the model surface at the
    ///     context <c>AddRaskData&lt;TContext&gt;(…)</c> registered.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    /// <remarks>
    ///     <para>
    ///         Call it <b>after</b> <c>UseAuthentication()</c> and before the endpoints: an endpoint then reads
    ///         <c>Current.UserId</c>, filters by the tenant in flight and stamps an insert with it, and an
    ///         <c>AddRaskTenant</c> resolver is asked as the request starts.
    ///     </para>
    ///     <para>
    ///         It is also the <c>Db.Configure(app.Services)</c> a host of its own used to call, so
    ///         <c>Product.Where(…)</c> works from here on — in a hosted service too, since those start later.
    ///         It opens no connection: nothing is created, migrated or checked.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><c>AddRaskData&lt;TContext&gt;(…)</c> was not called.</exception>
    public static IApplicationBuilder UseRaskData(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<AmbientContextBinding>() is null)
        {
            throw new InvalidOperationException(
                "UseRaskData() needs the services AddRaskData<TContext>(…) registers. Call " +
                "builder.Services.AddRaskData<AppDbContext>(o => o.UseSqlServer(connectionString)) before " +
                "building the app.");
        }

        Db.Configure(app.ApplicationServices);
        DataHostSeams.UseRequestScope(app);

        return app;
    }

    /// <summary>The read context on the database the app's own context was pointed at.</summary>
    private sealed class ReadContextFactory(
        IServiceProvider services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        : IDbContextFactory<RaskReadDbContext>
    {
        private readonly Lazy<DbContextOptions<RaskReadDbContext>> _options = new(() =>
        {
            var options = new DbContextOptionsBuilder<RaskReadDbContext>().UseApplicationServiceProvider(services);
            configure(services, options);
            return options.Options;
        });

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "The read context's constructor carries EF Core's own [RequiresUnreferencedCode]; this "
                            + "package is the server host, which is never trimmed.")]
        public RaskReadDbContext CreateDbContext() => new(_options.Value);
    }
}
