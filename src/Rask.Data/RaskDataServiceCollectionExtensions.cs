using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Rask.Data;

/// <summary>Registers Rask.Data's EF Core interceptors into an <see cref="IServiceCollection"/>.</summary>
public static partial class RaskDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the auditing, soft-delete, and domain-event interceptors as
    /// <see cref="ISaveChangesInterceptor"/> services, plus a <see cref="TimeProvider"/>. Add them to a
    /// context with
    /// <c>o.AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;())</c> in your
    /// <c>AddDbContext(Factory)</c> callback, and call <c>modelBuilder.ApplyRaskConventions()</c> in
    /// <c>OnModelCreating</c>. Idempotent. Domain-event dispatch needs <c>AddRaskCqrs()</c>.
    /// </summary>
    /// <remarks>
    /// A raised event reaches its <c>IEventHandler</c>s in memory after the save commits, and its
    /// <c>IDurableHandler</c>s through the outbox when <c>Rask.Outbox</c> is registered — each handler chooses.
    /// </remarks>
    /// <param name="services">The services.</param>
    /// <param name="configure">
    /// Sets the writes' options — <c>o =&gt; o.OneAggregatePerTransaction = AggregatePolicy.Throw</c>. Every call's applies,
    /// in order.
    /// </param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddRaskData(this IServiceCollection services, Action<RaskDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RaskDataOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too

        AddSaveInterceptors(services);

#pragma warning disable S3251 // implemented only by the browser build (Browser/); a server build has nothing to wire
        AddBrowserWiring(services);
#pragma warning restore S3251

        return services;
    }

    /// <summary>
    ///     Registers the interceptors as <see cref="AddRaskData" /> does, and binds
    ///     <typeparamref name="TContext" /> as the context the model surface opens — the one behind
    ///     <c>Product.Where(…)</c> and <c>Product.Create(model)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Naming the context here, rather than scanning for one, is what lets an app with two
    ///         databases say which one the bare <c>Product.Where(…)</c> means. It is resolved through
    ///         <see cref="IDbContextFactory{TContext}" />, so the app's own
    ///         <c>AddDbContextFactory&lt;TContext&gt;</c> — with its interceptors and its connection
    ///         string — is what actually opens the context.
    ///     </para>
    ///     <para>
    ///         One line still has to run after the container is built, to hand the model surface its
    ///         factory. A Rask app gets that from the host; anything else calls
    ///         <see cref="Db.Configure(IServiceProvider)" />:
    ///     </para>
    ///     <code>
    /// builder.Services.AddRaskData&lt;AppDbContext&gt;();
    /// builder.Services.AddDbContextFactory&lt;AppDbContext&gt;((sp, o) =&gt; o
    ///     .UseRaskSqlite(sp)
    ///     .AddInterceptors(sp.GetServices&lt;ISaveChangesInterceptor&gt;()));
    ///
    /// var app = builder.Build();
    /// Db.Configure(app.Services);
    ///     </code>
    /// </remarks>
    public static IServiceCollection AddRaskData<[DynamicallyAccessedMembers(DataTrimming.Context)] TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskData();

        // Singleton and resolved lazily: IDbContextFactory<TContext> is itself a singleton, so the
        // binding never reaches into a request or session scope for the context it opens. TryAdd keeps
        // the first binding, so a second call naming another context does not silently repoint the
        // model surface out from under the first.
        services.TryAddSingleton(sp => new AmbientContextBinding(
            typeof(TContext),
            () => sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext()));

        // A HasNonOverlappingRange or HasFullTextSearch the provider would silently ignore fails the boot rather
        // than the first double booking or search. TryAddEnumerable, so a second call for the same context checks once.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ProviderFeatureCheck<TContext>>());

        return services;
    }

    // After the stamps and versions, so a declared numbering reads the rows as they go out.
    private static void AddDeclaredWrites(IServiceCollection services) =>
        services.AddSingleton<ISaveChangesInterceptor, NumberingInterceptor>();

    // Rask.Data's browser build fills this in (Browser/BrowserData.cs); on a server the Rask host wires the same.
#pragma warning disable S3251 // implemented only by the browser build (Browser/)
    static partial void AddBrowserWiring(IServiceCollection services);
#pragma warning restore S3251

    // Registration order is the interception order, once per container.
    private static void AddSaveInterceptors(IServiceCollection services)
    {
        // Registration order is the interception order: soft-delete rewrites Deleted -> Modified first, so
        // auditing then stamps + versions the resulting update.
        if (!services.Any(static d => d.ImplementationType == typeof(SoftDeleteInterceptor)))
        {
            // Not a save interceptor at heart: it joins the app's own context to a Db.Transaction when it opens its
            // connection, so raw SQL inside the block runs inside it. Registered here so every host's contexts carry it.
            services.AddSingleton<ISaveChangesInterceptor>(AmbientTransactionInterceptor.Instance);

            // Lazy loading off unless the context's options AllowLazyLoading(): a navigation read in a loop, a render or a
            // validation would otherwise be a query nobody wrote.
            services.AddSingleton<ISaveChangesInterceptor>(LazyLoadingGuard.Instance);

            // First, so a child collection declared `= []` is one the others, and EF Core, can write into.
            services.AddSingleton<ISaveChangesInterceptor>(WritableChildrenInterceptor.Instance);

            // Before soft-delete turns a removal into an update, and before the outbox and the publish read the rows'
            // events: what an aggregate's OnDelete() raises goes out with its delete.
            services.AddSingleton<ISaveChangesInterceptor>(DeleteHookInterceptor.Instance);

            // Before soft-delete turns a removal into an update, and before the outbox and the publish read the rows'
            // events: a declared projection's event rides on its rows, so it is stored and published as theirs.
            services.AddSingleton<ISaveChangesInterceptor>(ProjectionInterceptor.Instance);
            services.AddSingleton<ISaveChangesInterceptor, SoftDeleteInterceptor>();

            // Before auditing, so a gated member it keeps unwritten is not what makes the row look changed.
            services.AddSingleton<ISaveChangesInterceptor, GatedWritesInterceptor>();
            services.AddSingleton<ISaveChangesInterceptor, AuditingInterceptor>();

            AddDeclaredWrites(services);

            // Publishes after the commit and clears the events only then, so the outbox (which reads the same events
            // in SavingChanges) sees them whichever interceptor runs first.
            services.AddSingleton<ISaveChangesInterceptor, DomainEventInterceptor>();

            // After the others have shaped the save, so it counts the rows the save writes: a second aggregate in one
            // Db.Transaction, written without manyAggregates:, meets RaskDataOptions.OneAggregatePerTransaction.
            services.AddSingleton<ISaveChangesInterceptor, OneAggregatePerTransactionInterceptor>();

            // Last, so it sees the save as the others left it. Also a transaction interceptor — EF hands the
            // same instance both roles — which is how a save inside the caller's transaction waits for its
            // commit before anyone refetches.
            services.AddSingleton<ISaveChangesInterceptor, DataChangesInterceptor>();

            // After every other: it replaces a refusal the model has a sentence for — a unique index's violation,
            // the delete of a referenced row — by throwing, and an interceptor after it would not be told the
            // save failed.
            services.AddSingleton<ISaveChangesInterceptor, ViolationMessageInterceptor>();
        }
    }
}
