using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core.Routing;
using Rask.Dashboard.Logging;
using Rask.Dashboard.Panels;
using Rask.Hosting.Shared;

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
    /// <para>
    /// <see cref="RaskDashboardOptions"/> reads the <c>Rask:Dashboard</c> configuration section first and then
    /// <paramref name="configure"/>, so code wins. That includes
    /// <see cref="RaskDashboardOptions.AllowAnonymousAccess"/>: an environment variable can open the console,
    /// which is the point of it being configuration, and a reason to keep the deploy environment's variables
    /// as guarded as its code.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the battery tables.</typeparam>
    public static IServiceCollection AddRaskDashboard<TContext>(
        this IServiceCollection services,
        Action<RaskDashboardOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRaskOptions<RaskDashboardOptions>("Rask:Dashboard", static (section, o) => section.Bind(o),
            configure, static o => o.Validate());
        services.TryAddSingleton(TimeProvider.System);

        // The console is its OWN application, not a set of pages inside the host's. RouteRegistry is
        // process-wide, so referencing this package used to be enough to put these pages in the host
        // app's table -- which meant the host's root rendered them, inside the host's document. Sharing
        // a document is not cosmetic: the console's stylesheet then applied to the host's own pages, and
        // the host's [NotFound] answered a mistyped console URL.
        //
        // TryAddEnumerable keyed on the implementation instance would not dedupe, so a repeated
        // AddRaskDashboard call is guarded by looking for THIS console's mount. Only this one: the guard
        // used to skip on any RaskMountedApp at all, so a second application mounted first — another
        // package's own console — silently took the dashboard off the host with nothing reporting it.
        //
        // Keyed descriptors are skipped before ImplementationInstance is read, because that property THROWS
        // on a keyed descriptor — a host that registered some keyed mount would otherwise fail to start here.
        if (!services.Any(d => d.ServiceType == typeof(RaskMountedApp)
                               && !d.IsKeyedService
                               && d.ImplementationInstance is RaskMountedApp { Root: var root }
                               && root == typeof(RaskDashboardShell)))
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
        // AddRaskDashboard call doesn't double-capture every entry. Registered whether or not capture is on:
        // Rask:Dashboard:CaptureLogs is not readable until the options are built, so the provider reads it
        // and hands out a logger that drops everything when it is off.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, DashboardLoggerProvider>());

        // One adapter per battery. Each decides at request time whether it has anything to show, so the
        // registration stays unconditional and the app's own wiring is the single source of truth.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, JobsQueuePanel<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, OutboxQueuePanel<TContext>>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IQueuePanel, MailQueuePanel<TContext>>());
        services.AddScoped<ICachePanelReader, CachePanel<TContext>>();
        services.AddScoped<ISystemPanelReader, SystemPanel<TContext>>();

        AddDefaultPolicy(services);
        return services;
    }

    // PostConfigure runs after every AddAuthorization(...) delegate the app registered, whatever the order
    // of the AddRaskDashboard call — so this fills a gap rather than overwriting an intent. Taking
    // IHostEnvironment and the built options as dependencies is what lets the default differ by environment
    // and by configuration without the extension method needing either at registration time.
    private static void AddDefaultPolicy(IServiceCollection services)
    {
        // AddAuthorizationCore, not AddAuthorization: the latter lives in the ASP.NET shared framework, and
        // this package deliberately takes no FrameworkReference. The core registration is what supplies the
        // IAuthorizationPolicyProvider and IAuthorizationService that RouteAuthorizationGuard resolves; the
        // host's own AddRask() calls the full AddAuthorization() anyway.
        services.AddAuthorizationCore();
        services.AddOptions<AuthorizationOptions>()
            .PostConfigure<IHostEnvironment, DashboardSecurityState, RaskDashboardOptions>(
                static (authz, environment, state, options) =>
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
