using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Server.DevTools;

namespace Rask.DevTools.Endpoints;

/// <summary>
///     The devtools' endpoints on a Server host: the host script a page loads into itself, and the URL a
///     page names it by.
/// </summary>
internal sealed class DevToolsServerEndpoints : IRaskServerDevTools
{
    /// <summary>Everything the devtools serve lives under this prefix, their own panel pages included.</summary>
    internal const string Prefix = "/_rask-devtools";

    /// <summary>Where the host script is served, under the app's path base.</summary>
    internal const string HostScriptPath = Prefix + "/host.js";

    // Both set once, while the app maps its endpoints and before it serves a request; null / empty until then,
    // and for good outside Development.
    private string? _hostScriptUrl;
    private PathString _prefix;

    public void MapEndpoints(IEndpointRouteBuilder endpoints, string pathBase)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Development only, decided once at startup. Outside it nothing is mapped at all, so to anything
        // probing, a Debug build running in Production looks exactly like one that never carried the tools.
        if (endpoints.ServiceProvider.GetService<IHostEnvironment>()?.IsDevelopment() != true)
        {
            return;
        }

        // A RequestDelegate, not a minimal-API Delegate, for the reason UseRask gives for its own endpoints:
        // RequestDelegateFactory is RequiresDynamicCode, and nothing generates this library's delegates.
        var script = DevToolsScripts.LoadHost();
        endpoints.MapGet(pathBase + HostScriptPath, (RequestDelegate)(ctx =>
                Results.Text(script, "text/javascript; charset=utf-8").ExecuteAsync(ctx)))
            // The script carries nothing about anyone, and an app with a fallback authorization policy still
            // has to load its own tools — the same call the content-addressed asset endpoints make.
            .AllowAnonymous();

        _prefix = new PathString(pathBase + Prefix);
        _hostScriptUrl = pathBase + HostScriptPath;
    }

    public string? HostScriptUrl(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Never on the devtools' own pages. The panel is a Rask page like any other; stamped, it would load its
        // own pill inside its own frame and inspect itself.
        return _hostScriptUrl is null || context.Request.Path.StartsWithSegments(_prefix)
            ? null
            : _hostScriptUrl;
    }
}
