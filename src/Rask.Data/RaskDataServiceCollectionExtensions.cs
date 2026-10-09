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
    public static IServiceCollection AddRaskData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too

        // Registration order is the interception order: soft-delete rewrites Deleted -> Modified first, so
        // auditing then stamps + versions the resulting update.
        if (!services.Any(static d => d.ImplementationType == typeof(SoftDeleteInterceptor)))
        {
            services.AddSingleton<ISaveChangesInterceptor, SoftDeleteInterceptor>();
            services.AddSingleton<ISaveChangesInterceptor, AuditingInterceptor>();

            // Publishes after the commit and clears the events only then, so the outbox (which reads the same events
            // in SavingChanges) sees them whichever interceptor runs first.
            services.AddSingleton<ISaveChangesInterceptor, DomainEventInterceptor>();

            // Last, so it sees the save as the others left it. Also a transaction interceptor — EF hands the
            // same instance both roles — which is how a save inside the caller's transaction waits for its
            // commit before anyone refetches.
            services.AddSingleton<ISaveChangesInterceptor, DataChangesInterceptor>();

            // After every other: it replaces a unique index's violation with the message the index carries, by
            // throwing — and an interceptor after it would not be told the save failed.
            services.AddSingleton<ISaveChangesInterceptor, UniqueViolationInterceptor>();
        }

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

    // Rask.Data's browser build fills this in (Browser/BrowserData.cs); on a server the Rask host wires the same.
#pragma warning disable S3251 // implemented only by the browser build (Browser/)
    static partial void AddBrowserWiring(IServiceCollection services);
#pragma warning restore S3251
}
