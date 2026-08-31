using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core.Routing;
using Rask.Dashboard.Logging;
using Rask.Dashboard.Panels;

namespace Rask.Dashboard;

/// <summary>The authorization policy the dashboard's pages are gated on.</summary>
public static class RaskDashboardPolicies
{
    /// <summary>
    /// The policy name every dashboard page carries. Define it in your own <c>AddAuthorization</c> to say
    /// who may operate the app:
    /// <code>
    /// builder.Services.AddAuthorization(o =>
    ///     o.AddPolicy(RaskDashboardPolicies.Access, p => p.RequireRole("Admin")));
    /// </code>
    /// If you don't, the dashboard supplies a default — permissive in Development, <b>deny-all</b>
    /// everywhere else.
    /// </summary>
    public const string Access = "RaskDashboard";
}

/// <summary>Registers the batteries dashboard into an <see cref="IServiceCollection"/>.</summary>
public static class RaskDashboardServiceCollectionExtensions
{
    /// <summary>
    ///     Where the console is served. Fixed, not configurable: "the console is at /_rask" is meant to be
    ///     something you know rather than something you look up, and it has to agree with the
    ///     <c>[Route("_rask")]</c> on <c>DashboardLayout</c> that the pages themselves are registered under.
    /// </summary>
    internal const string DashboardPattern = "/_rask/{**path}";

    /// <summary>
    /// Mounts the operator dashboard at <c>/_rask</c>, reading the battery tables owned by
    /// <typeparamref name="TContext"/>. Call it after the <c>AddRaskX&lt;TContext&gt;()</c> registrations —
    /// a panel appears only when its battery is both registered and mapped in the model, so an app with
    /// only jobs gets only the jobs panel.
    /// <para>
    /// Access is gated on the <see cref="RaskDashboardPolicies.Access"/> policy. Define it yourself, or get
    /// the fail-closed default: permissive in Development, deny-all in every other environment. Calling
    /// this does not open anything by accident.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the battery tables.</typeparam>
    public static IServiceCollection AddRaskDashboard<TContext>(
        this IServiceCollection services,
        Action<RaskDashboardOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RaskDashboardOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        // The console is its OWN application, not a set of pages inside the host's. RouteRegistry is
        // process-wide, so referencing this package used to be enough to put these pages in the host
        // app's table -- which meant the host's root rendered them, inside the host's document. Sharing
        // a document is not cosmetic: the console's stylesheet then applied to the host's own pages, and
        // the host's [NotFound] answered a mistyped console URL.
        //
        // TryAddEnumerable keyed on the implementation instance would not dedupe, so this is guarded by
        // the options singleton above: a repeated AddRaskDashboard call finds the options already
        // registered and would otherwise mount the console twice.
        if (!services.Any(d => d.ServiceType == typeof(RaskMountedApp)))
        {
            services.AddSingleton(new RaskMountedApp(
                typeof(RaskDashboardShell),
                DashboardPattern,
                typeof(RaskDashboardShell).Assembly));
        }
        services.TryAddSingleton<DashboardSecurityState>();
        services.TryAddSingleton<DashboardLogBuffer>();

        // Registered as a logging provider rather than a bespoke channel, so the log panel sees exactly
        // what every other sink sees. TryAddEnumerable keys on the implementation type, so a repeated
        // AddRaskDashboard call doesn't double-capture every entry.
        if (options.CaptureLogs)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<ILoggerProvider, DashboardLoggerProvider>());
        }

        // One adapter per battery. Each decides at request time whether it has anything to show, so the
        // registration stays unconditional and the app's own wiring is the single source of truth.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, JobsQueuePanel<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, OutboxQueuePanel<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, MailQueuePanel<TContext>>());
        services.AddScoped<ICachePanelReader, CachePanel<TContext>>();
        services.AddScoped<ISystemPanelReader, SystemPanel<TContext>>();

        AddDefaultPolicy(services, options);
        return services;
    }

    // PostConfigure runs after every AddAuthorization(...) delegate the app registered, whatever the order
    // of the AddRaskDashboard call — so this fills a gap rather than overwriting an intent. Taking
    // IHostEnvironment as a dependency is what lets the default differ by environment without the
    // extension method needing the environment at registration time.
    private static void AddDefaultPolicy(IServiceCollection services, RaskDashboardOptions options)
    {
        // AddAuthorizationCore, not AddAuthorization: the latter lives in the ASP.NET shared framework, and
        // this package deliberately takes no FrameworkReference. The core registration is what supplies the
        // IAuthorizationPolicyProvider and IAuthorizationService that RouteAuthorizationGuard resolves; the
        // host's own AddRask() calls the full AddAuthorization() anyway.
        services.AddAuthorizationCore();
        services.AddOptions<AuthorizationOptions>()
            .PostConfigure<IHostEnvironment, DashboardSecurityState>((authz, environment, state) =>
            {
                if (authz.GetPolicy(RaskDashboardPolicies.Access) is not null)
                {
                    return; // the app said who may operate it — never second-guess that
                }

                var open = options.AllowAnonymousAccess || environment.IsDevelopment();
                state.UsingFallbackPolicy = true;
                state.FallbackIsOpen = open;
                authz.AddPolicy(RaskDashboardPolicies.Access, policy => policy.RequireAssertion(_ => open));
            });
    }
}
