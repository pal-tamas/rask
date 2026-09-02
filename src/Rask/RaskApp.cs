using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Server;
using Rask.Wasm.Hosting;

namespace Rask;

/// <summary>
/// A Rask application: the host and every battery, wired.
/// </summary>
/// <remarks>
/// <para>
/// The point is what is <b>not</b> in your <c>Program.cs</c>. A scaffolded app used to carry three hundred
/// lines describing the framework rather than the app — a middleware order whose comments were really
/// instructions for avoiding silent failures, and one <c>AddRaskX</c> per battery. Every one of those is
/// the same in every app, so it belongs here, and what is left is only what is true about yours.
/// </para>
/// <para>
/// <b>Every battery is on.</b> Referencing this package is what turns them on; there is nothing to opt
/// into. An app does without one by saying so:
/// </para>
/// <example>
/// <code>
/// var app = RaskApp.Create(args);
///
/// app.Configure(c =>
/// {
///     c.Jobs.Off();                                            // no background work here
///     c.Mail.Configure(o => o.From = "no-reply@example.com");
/// });
///
/// app.Services.AddScoped&lt;PopularProducts&gt;();
/// app.MapEndpoints(e =&gt; e.MapPushSubscriptions());
///
/// app.Run&lt;App&gt;();
/// </code>
/// </example>
/// <para>
/// It wraps <see cref="WebApplicationBuilder"/> rather than replacing it: <see cref="Services"/> is the
/// same collection, <see cref="Create"/> hands you the builder itself, and <c>AddRask</c>/<c>UseRask</c>
/// remain public. This is a layer over them.
/// </para>
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

    /// <summary>Creates the host with Rask's services registered and its host defaults applied.</summary>
    /// <param name="args">The process arguments, forwarded to <see cref="WebApplication.CreateBuilder(string[])"/>.</param>
    /// <param name="configure">
    /// An escape hatch onto the underlying <see cref="WebApplicationBuilder"/>, run before <c>AddRask</c> —
    /// for anything ASP.NET-shaped that has no place on this type.
    /// </param>
    public static RaskApp Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);

        // NOT AddRask — that waits for Build. Its options go in with TryAddSingleton, so the first call
        // wins and a second one is silently discarded (RASK056). Calling it here would freeze the culture
        // list and the render-mode ceiling before Configure had a chance to say anything, and the app
        // would ship with no languages while its Program.cs plainly listed some.
        builder.Services.AddHealthChecks();
        return new RaskApp(builder);
    }

    /// <summary>Says how this app differs from a default one — which batteries it does without, and how the rest are set up.</summary>
    public RaskApp Configure(Action<RaskAppOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_options);
        return this;
    }

    /// <summary>
    /// Maps your own endpoints — a named place for them, next to the rest of the app's composition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a place, not a fix. Endpoint routing matches on <em>precedence</em>, never on registration
    /// order, and Rask's catch-all is the least specific pattern there is (<c>/{**path}</c>), so a route
    /// mapped after <c>UseRask</c> still wins for the paths it names. Mapping through here rather than on
    /// the built <see cref="WebApplication"/> buys ordering only against genuine <em>middleware</em>, and
    /// keeps the app's endpoints in one readable spot.
    /// </para>
    /// <para>
    /// What the catch-all does swallow is a request matching <b>nothing</b>: an API path with a typo, or
    /// one whose route was deleted, renders the app with a 200 instead of answering 404. That is a real
    /// failure and this seam does not prevent it — see <c>docs/api-endpoints.md</c>.
    /// </para>
    /// </remarks>
    public RaskApp MapEndpoints(Action<IEndpointRouteBuilder> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _endpoints.Add(map);
        return this;
    }

    /// <summary>Builds the pipeline and runs the app, with <typeparamref name="TApp"/> as the root component.</summary>
    public void Run<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component =>
        Build<TApp>(pathBase).Run();

    /// <summary>The awaitable <see cref="Run{TApp}"/>.</summary>
    public Task RunAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component =>
        Build<TApp>(pathBase).RunAsync();

    /// <summary>Builds the <see cref="WebApplication"/> and applies the pipeline, without running it.</summary>
    public WebApplication Build<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        string pathBase = "")
        where TApp : Component
    {
        // The live runtime, now that Configure has had its say. One call, because a second is dropped.
        _builder.Services.AddRask(
            configure: _options.Live,
            configureServer: o =>
            {
                if (_options.Wasm)
                {
                    o.RenderModes.Wasm = true;
                }

                _options.Server?.Invoke(o);
            },
            configureCulture: _options.Cultures.Count == 0
                ? null
                : c =>
                {
                    foreach (var culture in _options.Cultures)
                    {
                        c.SupportedCultures.Add(culture);
                    }
                });

        if (_options.Wasm)
        {
            _builder.Services.AddRaskWasmHost();
        }

        // The batteries, LAST — after every Configure block and after anything Program.cs registered
        // itself. Both halves matter: the off-switches are only known now, and every AddRaskX is
        // idempotent, so an app that called one directly has already won.
        RaskBatteryWiring.Apply(_builder, _options);

        var app = _builder.Build();

        // FIRST: rewrite Request.Scheme and RemoteIpAddress from the proxy's headers, so everything below
        // — HSTS, redirects, the app's own logging — sees the request the visitor actually made. Opt-in,
        // because trusting these from an arbitrary client lets it forge its own IP.
        if (_options.BehindProxy)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            });
        }

        // Terminal middleware, so it short-circuits before UseHttpsRedirection and answers over plain HTTP.
        // `rask deploy` probes this internally with no X-Forwarded-Proto, where a redirected endpoint would
        // 307 to a port nothing is listening on and fail the blue-green gate.
        app.UseHealthChecks(_options.HealthPath);

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler(_options.ErrorPath);
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        // MapStaticAssets THROWS when the build produced no static-asset manifest, which is ordinary for a
        // host that is not a Web SDK project — a test host above all. As the default path, failing to boot
        // there with a stack trace from inside routing is a poor first experience.
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

        // Before anything opens the database: a restore that runs after the first query has already lost,
        // and the failure is a fresh empty database on a machine that was supposed to have recovered.
        if (_options.RunBeforeDatabaseOpensAsync is { } restore)
        {
            restore(app.Services).GetAwaiter().GetResult();
        }

        // Must precede UseRask so HttpContext.User is populated on the initial GET and the WebSocket
        // upgrade — otherwise the principal is empty at both and every authorized page challenges
        // (RASK024). Conditional, because UseAuthentication throws when no scheme is registered.
        if (app.Services.GetService<IAuthenticationSchemeProvider>() is not null)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        foreach (var map in _endpoints)
        {
            map(app);
        }

        if (_options.Wasm)
        {
            // Serves the browser bundle this project publishes into wwwroot, ahead of an explicit
            // UseRouting — the bundle's own assets have to be reachable before the catch-all claims
            // everything below it.
            app.UseRaskWasmAssets();
            app.UseRouting();
        }

        app.UseRask<TApp>(pathBase: pathBase);
        return app;
    }
}
