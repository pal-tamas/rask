using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Core.Diagnostics;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Probe;
using Rask.Server;
using Rask.Server.Authentication;
using Rask.Server.DevTools;

namespace Rask.DevTools.Endpoints;

/// <summary>
///     The devtools on a Server host: the host script, the tag a page loads it with, and who may open the panel.
/// </summary>
internal sealed class DevToolsServerEndpoints : IRaskServerDevTools
{
    /// <summary>Everything the devtools serve lives under this prefix, their own panel pages included.</summary>
    internal const string Prefix = DevToolsProbe.PanelPrefix;

    /// <summary>Where the host script is served, under the app's path base.</summary>
    internal const string HostScriptPath = Prefix + "/host.js";

    /// <summary>Set to <c>1</c> to open the panel from another machine — a phone on the LAN, a VM, a container.</summary>
    internal const string AllowRemoteVariable = "RASK_DEVTOOLS_ALLOW_REMOTE";

    private readonly DevToolsPanelTokens _tokens = new();
    private readonly Func<bool> _uiKitAvailable;

    // All set once, while the app maps its endpoints and before it serves a request; null / empty until then, and for
    // good outside Development.
    private string? _hostScriptUrl;
    private string? _panelUrl;
    private PathString _prefixUnderBase;
    private bool _allowRemote;

    /// <summary>The endpoints the container builds: the panel is on when the app carries Rask.Ui.</summary>
    public DevToolsServerEndpoints()
        : this(DevToolsUiKit.IsAvailable)
    {
    }

    /// <summary>With the kit check supplied, so a test can stand in for an app without Rask.Ui.</summary>
    internal DevToolsServerEndpoints(Func<bool> uiKitAvailable) => _uiKitAvailable = uiKitAvailable;

    public void MapEndpoints(IEndpointRouteBuilder endpoints, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Development only, decided once at startup. Outside it nothing is mapped and every panel request is refused as
        // not found, so to anything probing, a Debug build running in Production looks like one without the tools.
        if (endpoints.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() != true)
        {
            return;
        }

        // The panel is drawn with the app's own Rask.Ui; the package brings none, because as a dependency it rode into
        // every Release publish of an app that never asked for it. Without the kit there is no panel to open, so the
        // devtools stay off — and say so once, instead of a pill that opens a broken frame.
        if (!_uiKitAvailable())
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning,
                "Rask.DevTools",
                "Rask DevTools are off: their panel is drawn with Rask.Ui, which this app does not reference. Add a "
                + "PackageReference to Rask.Ui to turn them on — apps created with `rask new` and the Rask package already "
                + "have one.");
            return;
        }

        // The probe the runtime reports to, now that the devtools are on. Installed here, where both conditions were just
        // decided, and taken back out when this host stops — only if it is still this host's — so a process that builds
        // another host afterwards (a test run, an in-process restart) never reports into a stopped host's feeds.
        var probe = endpoints.ServiceProvider.GetRequiredService<DevToolsProbe>();
        RaskDevToolsHook.Probe = probe;
        endpoints.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(() =>
        {
            if (ReferenceEquals(RaskDevToolsHook.Probe, probe))
            {
                RaskDevToolsHook.Probe = null;
            }
        });

        // A RequestDelegate, not a minimal-API Delegate, for the reason UseRask gives for its own endpoints:
        // RequestDelegateFactory is RequiresDynamicCode, and nothing generates this library's delegates.
        var script = DevToolsScripts.LoadHost();
        endpoints.MapGet(pathBase + HostScriptPath, (RequestDelegate)(ctx =>
                Results.Text(script, "text/javascript; charset=utf-8").ExecuteAsync(ctx)))
            // The script carries nothing about anyone, and an app with a fallback authorization policy still has to
            // load its own tools — the same call the content-addressed asset endpoints make.
            .AllowAnonymous();

        _prefixUnderBase = new PathString(pathBase + Prefix);
        _hostScriptUrl = pathBase + HostScriptPath;
        _panelUrl = pathBase + Prefix + "/";
        _allowRemote = Environment.GetEnvironmentVariable(AllowRemoteVariable) == "1";
    }

    public DevToolsPageTag? PageTag(HttpContext context, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        // Never on the devtools' own pages. The panel is a Rask page like any other; tagged, it would load its own pill
        // inside its own frame and inspect itself.
        if (_hostScriptUrl is null || context.Request.Path.StartsWithSegments(_prefixUnderBase))
        {
            return null;
        }

        return new DevToolsPageTag(
            _hostScriptUrl,
            _panelUrl + "?inspect=" + Uri.EscapeDataString(sessionId) + "&t=" + _tokens.For(sessionId));
    }

    public int? RefusePanel(HttpContext context, string path)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Owns(path))
        {
            return null;
        }

        // Outside Development the devtools own nothing, so their pages are as absent as any other unknown path.
        if (_hostScriptUrl is null)
        {
            return StatusCodes.Status404NotFound;
        }

        // A panel shows another session's state live, so by default only this machine may open one. An unknown address
        // is not assumed to be local.
        if (!_allowRemote && !IsLoopback(context.Connection.RemoteIpAddress))
        {
            return StatusCodes.Status403Forbidden;
        }

        // The token says the panel was opened from the page the server rendered for that session. A wrong or missing
        // one is answered like a session that does not exist, so neither can be probed for.
        var query = context.Request.Query;
        if (FindInspected(
                context.RequestServices.GetRequiredService<LiveSessionStore>(),
                query["inspect"].ToString(),
                query["t"].ToString()) is not { } inspected)
        {
            return StatusCodes.Status404NotFound;
        }

        // The same binding upload and download make: the request must come from the page's own origin, and carry the
        // identity of whoever owns the inspected session.
        if (!RaskEndpointExtensions.IsSameOrigin(context.Request)
            || !RaskEndpointExtensions.SameSessionUser(
                context.User, inspected.Services.GetRequiredService<SessionUserProvider>().Current))
        {
            return StatusCodes.Status403Forbidden;
        }

        return null;
    }

    public bool CanResume(string path) => !Owns(path);

    /// <summary>
    ///     The session <paramref name="sessionId" /> names, when the devtools are on and <paramref name="token" /> is that
    ///     session's token; null otherwise. Who is asking is the caller's check.
    /// </summary>
    internal LiveSession? FindInspected(LiveSessionStore store, string? sessionId, string? token) =>
        _hostScriptUrl is not null && _tokens.Verify(sessionId, token) ? store.Get(sessionId!) : null;

    private static bool IsLoopback(IPAddress? address) => address is not null && IPAddress.IsLoopback(address);

    private static bool Owns(string? path) =>
        path is not null && new PathString(path).StartsWithSegments(Prefix);
}
