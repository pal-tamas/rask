using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     A Rask.Server app on a loopback port, in this process, for a real browser to load.
/// </summary>
/// <remarks>
///     <para>
///         <paramref name="blockWebSockets" /> refuses the WebSocket upgrade with a <c>403</c> before Rask sees it —
///         what a corporate proxy that strips the upgrade does, and what makes a browser's socket fail before it
///         opens. Every attempt is counted, so a journey can prove a tab stopped trying.
///     </para>
///     <para>
///         <paramref name="staticFiles" /> serves <c>wwwroot</c> as an app's static-file middleware does — off by
///         default, because most journeys here have no file to serve and one proves what a missing file does.
///     </para>
///     <para>
///         Port 0, read back from the server, so a run cannot collide with a dev server or another worktree's gate.
///     </para>
/// </remarks>
internal sealed class LiveServerHost : IAsyncDisposable
{
    private const string WebSocketPath = "/rask/ws";

    private readonly WebApplication _app;
    private int _webSocketAttempts;

    /// <summary>The scheme a host started with <c>cookieSignIn</c> signs its readers in under.</summary>
    public const string CookieScheme = "Cookies";

    private LiveServerHost(WebApplication app) => _app = app;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>How many WebSocket upgrades a browser has asked for.</summary>
    public int WebSocketAttempts => Volatile.Read(ref _webSocketAttempts);

    public static async Task<LiveServerHost> StartAsync<TApp>(
        bool blockWebSockets, bool staticFiles = false, Action<Rask.Core.Live.RaskLiveOptions>? live = null,
        bool cookieSignIn = false, string pathBase = "", Action<WebApplication>? beside = null,
        string? environment = null, bool endpointRouting = false)
        where TApp : Component
    {
        // The wwwroot the build copied beside the tests: what a web project serves from its own folder.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            EnvironmentName = environment,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddRask(live, o => o.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200));

        if (cookieSignIn)
        {
            builder.Services.AddAuthentication(CookieScheme).AddCookie(CookieScheme);
            builder.Services.AddAuthorization();
        }

        var app = builder.Build();
        var host = new LiveServerHost(app);

        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.Equals(WebSocketPath, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref host._webSocketAttempts);
                if (blockWebSockets)
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next(ctx);
        });

        if (staticFiles)
        {
            app.UseStaticFiles();
        }

        app.UseRouting();
        if (cookieSignIn)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        app.UseWebSockets();
        // What the host serves beside the app: the pages of an older application, outside the app's path base.
        beside?.Invoke(app);
        // The endpoint-routing overload is what a host that composes its own pipeline calls: no WebApplication.
        if (endpointRouting)
        {
            ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).MapRask<TApp>(pathBase: pathBase);
        }
        else
        {
            app.MapRask<TApp>(pathBase: pathBase);
        }

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();
        host.BaseUrl = address.TrimEnd('/');
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();

        // The path base is one value for the whole process: a host mapped under one must not leave it behind.
        Rask.Core.Live.LiveOptions.PathBase = string.Empty;
    }
}
