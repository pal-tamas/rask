using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Server;

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
///         Port 0, read back from the server, so a run cannot collide with a dev server or another worktree's gate.
///     </para>
/// </remarks>
internal sealed class LiveServerHost : IAsyncDisposable
{
    private const string WebSocketPath = "/rask/ws";

    private readonly WebApplication _app;
    private int _webSocketAttempts;

    private LiveServerHost(WebApplication app) => _app = app;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>How many WebSocket upgrades a browser has asked for.</summary>
    public int WebSocketAttempts => Volatile.Read(ref _webSocketAttempts);

    public static async Task<LiveServerHost> StartAsync<TApp>(bool blockWebSockets)
        where TApp : Component
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddRask(configureServer: o => o.ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200));

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

        app.UseRouting();
        app.UseWebSockets();
        app.UseRask<TApp>();
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
    }
}
