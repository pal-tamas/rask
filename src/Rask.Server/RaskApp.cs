using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core;

namespace Rask.Server;

/// <summary>
/// A Rask web application: the host, wired the way a Rask app is nearly always wired.
/// </summary>
/// <remarks>
/// <para>
/// The point is what is <b>not</b> in your <c>Program.cs</c>. A scaffolded app used to carry three hundred
/// lines describing the framework rather than the app — forwarded headers, the health endpoint's position
/// relative to HTTPS redirection, a Data Protection key ring, a shutdown budget, and a middleware order
/// whose comments were really instructions for avoiding silent failures. Every one of those is the same in
/// every app, so it belongs here, and what is left in <c>Program.cs</c> is only what is true about yours.
/// </para>
/// <para>
/// It wraps <see cref="WebApplicationBuilder"/> rather than replacing it. <see cref="Services"/> is the
/// same collection, <see cref="Create"/> hands you the builder itself if you need it, and nothing stops
/// you from dropping back to <c>AddRask</c>/<c>UseRask</c> and composing the pipeline by hand — this is a
/// layer over them, not a replacement for them.
/// </para>
/// <example>
/// <code>
/// var app = RaskApp.Create(args);
///
/// app.Services.AddScoped&lt;PopularProducts&gt;();
/// app.MapEndpoints(e =&gt; e.MapPushSubscriptions());
///
/// app.Run&lt;App&gt;();
/// </code>
/// </example>
/// </remarks>
public sealed class RaskApp
{
    private readonly WebApplicationBuilder _builder;
    private readonly List<Action<IEndpointRouteBuilder>> _endpoints = [];
    private readonly RaskAppOptions _options = new();

    private RaskApp(WebApplicationBuilder builder) => _builder = builder;

    /// <summary>The application's services. The same collection <see cref="WebApplicationBuilder"/> exposes.</summary>
    public IServiceCollection Services => _builder.Services;

    /// <summary>The application's configuration.</summary>
    public IConfiguration Configuration => _builder.Configuration;

    /// <summary>The host environment.</summary>
    public IWebHostEnvironment Environment => _builder.Environment;

    /// <summary>
    /// Creates the host with Rask's services registered and its host defaults applied.
    /// </summary>
    /// <param name="args">The process arguments, forwarded to <see cref="WebApplication.CreateBuilder(string[])"/>.</param>
    /// <param name="configure">
    /// An escape hatch onto the underlying <see cref="WebApplicationBuilder"/>, run before
    /// <c>AddRask</c>. For anything ASP.NET-shaped that has no place on this type — a different
    /// configuration source, a Kestrel option, a logging provider.
    /// </param>
    public static RaskApp Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        var app = new RaskApp(builder);
        builder.Services.AddRask();
        builder.Services.AddHealthChecks();
        return app;
    }

    /// <summary>
    /// Configures how this app differs from a default one — and, once batteries are declared, which of
    /// them it does without.
    /// </summary>
    /// <remarks>
    /// One block rather than a scattering of <c>AddRaskX</c> callbacks, so there is a single place to read
    /// the answer to "how is this app not the standard one". Applies immediately; call it as many times as
    /// you like.
    /// </remarks>
    public RaskApp Configure(Action<RaskAppOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options);
        return this;
    }

    /// <summary>
    /// Maps your own endpoints. They are always mapped <b>before</b> Rask's catch-all, which is the only
    /// position at which they can be reached.
    /// </summary>
    /// <remarks>
    /// A named seam rather than a documented ordering rule. <c>UseRask</c> ends the pipeline with a
    /// catch-all that serves the app for anything unmatched, so a minimal API mapped after it is
    /// unreachable — it does not error, it simply never runs, and the app renders where the author
    /// expected JSON. Queuing the callback here makes that unrepresentable.
    /// </remarks>
    public RaskApp MapEndpoints(Action<IEndpointRouteBuilder> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _endpoints.Add(map);
        return this;
    }

    /// <summary>Builds the pipeline and runs the app, with <typeparamref name="TApp"/> as the root component.</summary>
    /// <typeparam name="TApp">The root <see cref="Component"/> rendered for every matched route.</typeparam>
    /// <param name="pathBase">
    /// Optional URL prefix, so two Rask servers can share one origin behind a reverse proxy.
    /// </param>
    public void Run<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component =>
        Build<TApp>(pathBase).Run();

    /// <summary>The awaitable <see cref="Run{TApp}"/>.</summary>
    /// <typeparam name="TApp">The root <see cref="Component"/> rendered for every matched route.</typeparam>
    /// <param name="pathBase">Optional URL prefix.</param>
    public Task RunAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component =>
        Build<TApp>(pathBase).RunAsync();

    /// <summary>
    /// Builds the <see cref="WebApplication"/> and applies the pipeline, without running it — for tests
    /// and for a host that wants to inspect or extend the result before starting.
    /// </summary>
    /// <typeparam name="TApp">The root <see cref="Component"/> rendered for every matched route.</typeparam>
    /// <param name="pathBase">Optional URL prefix.</param>
    public WebApplication Build<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component
    {
        var app = _builder.Build();

        // FIRST: rewrite Request.Scheme and RemoteIpAddress from the proxy's headers, so everything below
        // — HSTS, redirects, the app's own logging — sees the request the visitor actually made.
        //
        // OPT-IN, and it stays that way. Trusting these headers from an arbitrary client lets it forge its
        // own IP; it is only safe where a proxy is definitely in front, which is a fact about the
        // deployment that no code here can check.
        if (_options.BehindProxy)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            });
        }

        // Terminal middleware, so it short-circuits before UseHttpsRedirection and answers over plain HTTP.
        // `rask deploy` probes this internally on http://…:8080 with no X-Forwarded-Proto, where a
        // redirected endpoint would 307 to a port nothing is listening on and fail the blue-green gate.
        app.UseHealthChecks(_options.HealthPath);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(_options.ErrorPath);
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // MapStaticAssets THROWS when the build produced no static-asset manifest, which is the ordinary
        // case for a host that is not a Web SDK project — a test host above all. Since this facade is the
        // default path rather than something a user opted into, failing to boot there would be a poor
        // first experience with nothing explaining it. Skipped with a warning instead: an app whose CSS is
        // missing gets a line naming the reason, rather than a stack trace from inside routing.
        var manifest = Path.Combine(
            AppContext.BaseDirectory, $"{app.Environment.ApplicationName}.staticwebassets.endpoints.json");
        if (File.Exists(manifest))
        {
            app.MapStaticAssets();
        }
        else
        {
            app.Services.GetService<ILoggerFactory>()?.CreateLogger("Rask").LogWarning(
                "No static-asset manifest at {Manifest}, so wwwroot is not being served. Expected for a "
                + "test host; in a real app it means the project is not using the Web SDK.",
                manifest);
        }

        // Give a bare status code — a 404 from an unmatched route — a readable body instead of a blank page.
        app.UseStatusCodePages();

        // Before anything opens the database. Rask.Server cannot reference the SQLite packages, so the
        // restore itself stays in the app; this is the point in the sequence it belongs at.
        _options.RunBeforeDatabaseOpens?.Invoke(app.Services);

        // Must precede UseRask so HttpContext.User is populated on the initial GET render and on the
        // WebSocket upgrade — otherwise the principal is empty at both, and every authorized page
        // challenges (RASK024).
        //
        // Conditional on authentication actually being registered: UseAuthentication throws when it is
        // not, so calling it unconditionally would break every app that has no auth at all.
        if (app.Services.GetService<IAuthenticationSchemeProvider>() is not null)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        // The app's own endpoints, at the only position that works — see MapEndpoints.
        foreach (var map in _endpoints)
        {
            map(app);
        }

        app.UseRask<TApp>(pathBase: pathBase);
        return app;
    }
}
