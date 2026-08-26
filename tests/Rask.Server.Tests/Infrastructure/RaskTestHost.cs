using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask.Server.Tests.Infrastructure;

internal sealed class RaskTestHost : IDisposable
{
    private readonly WebApplication _app;

    private RaskTestHost(WebApplication app, TestServer server)
    {
        _app = app;
        Server = server;
        Http = server.CreateClient();
        WebSockets = server.CreateWebSocketClient();
        Store = server.Services.GetRequiredService<LiveSessionStore>();
    }

    public TestServer Server { get; }
    public HttpClient Http { get; }
    public WebSocketClient WebSockets { get; }
    public LiveSessionStore Store { get; }

    public IServiceProvider Services => Server.Services;

    public Uri WebSocketUri => new(new Uri(Server.BaseAddress, "/rask/ws").ToString().Replace("http://", "ws://"));

    /// <summary>
    ///     Stops the host the way a SIGTERM does — fires <c>ApplicationStopping</c>, then awaits every
    ///     hosted service's <c>StopAsync</c>. Lets a test observe the shutdown drain (and assert on what
    ///     is true *when the stop returns*) instead of disposing and losing the host.
    /// </summary>
    public Task StopAsync() => _app.StopAsync();

    public void Dispose()
    {
        Http.Dispose();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // Reset the static PathBase so a pathBase-configured host doesn't leak
        // into subsequent tests in the same AppDomain. Cheap; matches the
        // existing Diff-mode reset convention.
        LiveOptions.PathBase = string.Empty;

        // Same reason, and sharper: UseRask sets IsDevelopment with `??=`, so it is claimed by the FIRST
        // host in the process and never revised. Left set, one host built with environment "Development"
        // would decide what every later host in the run reports — and the dev-only behaviour gated on it
        // (the error overlay, the dev error page) would then be tested against somebody else's answer.
        LiveOptions.IsDevelopment = null;
    }

    public static RaskTestHost Create<TApp>(
        Action<IServiceCollection>? configureServices = null,
        Action<IApplicationBuilder>? configureMiddleware = null,
        string pathBase = "",
        Action<RaskServerOptions>? configureServer = null,
        LiveDiffMode diffMode = LiveDiffMode.Auto,
        string? environment = null,
        Action<RaskCultureOptions>? configureCulture = null)
        where TApp : Component
    {
        // Defaults to whatever WebApplication picks (Production under test, absent an env var).
        // Pass `environment` to exercise the Development-gated dev-time behaviour.
        var builder = environment is null
            ? WebApplication.CreateBuilder()
            : WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        // Per-host WS / grace-period limits (RaskServerLimits) AND the wire-payload shape (DiffMode,
        // carried on the LiveSessionStore) are seeded here, so each TestServer carries its own — tests
        // that assert on a specific diff/full payload pass diffMode instead of writing a process-global
        // static, which lets them run in parallel.
        builder.Services.AddRask(
            configure: live => live.DiffMode = diffMode,
            configureServer: configureServer,
            configureCulture: configureCulture);
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        configureMiddleware?.Invoke(app);
        app.UseRask<TApp>(pathBase: pathBase);

        app.StartAsync().GetAwaiter().GetResult();

        var server = app.GetTestServer();
        return new RaskTestHost(app, server);
    }
}
