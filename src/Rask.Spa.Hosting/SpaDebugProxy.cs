using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Spa.Hosting;

/// <summary>
///     The WebAssembly debug proxy for a host serving a Rask WebAssembly client in Development: what lets a browser
///     debugger stop on the client's C# (#1073).
/// </summary>
/// <remarks>
///     <para>
///         A standalone app runs under the SDK's dev server, which maps <c>/_framework/debug</c> itself. A host that
///         serves its client through <see cref="RaskSpaEndpointExtensions.UseRaskSpa" /> had nothing there, and the
///         request fell to the asset 404. This maps the same two routes the SDK's server does and answers them the
///         same way: <c>ws-proxy</c> starts <c>BrowserDebugHost.dll</c> for the browser named in <c>?browser=</c> and
///         redirects the debugger's socket to it, so VS Code's <c>inspectUri</c> works unchanged against either.
///     </para>
///     <para>
///         The proxy's path is written by the client's build next to its static-web-assets manifest
///         (<c>{name}.rask-debugproxy</c>), because only the client's evaluation knows where the WebAssembly SDK
///         pack is. No file, no routes — a host whose client was built without it falls back to the 404 it had.
///     </para>
///     <para>
///         <b>Loopback only.</b> The proxy connects to whatever DevTools endpoint the request names, so a
///         <c>browser</c> that is not on this machine is refused, and the proxy itself listens on 127.0.0.1.
///     </para>
/// </remarks>
internal static partial class SpaDebugProxy
{
    /// <summary>The route the SDK's dev server uses, which <c>inspectUri</c> templates name.</summary>
    internal const string DebugPath = "_framework/debug";

    private const string ManifestSuffix = ".staticwebassets.runtime.json";

    /// <summary>
    ///     Where the client's build wrote the proxy's path, beside <paramref name="devManifest" />; null for a
    ///     manifest that is not a build output's.
    /// </summary>
    internal static string? PathFileFor(string devManifest) =>
        devManifest.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase)
            ? devManifest[..^ManifestSuffix.Length] + ".rask-debugproxy"
            : null;

    /// <summary>The proxy dll the build recorded, when it recorded one that exists.</summary>
    internal static string? ResolveHost(string devManifest)
    {
        if (PathFileFor(devManifest) is not { } pathFile || !File.Exists(pathFile))
        {
            return null;
        }

        var host = File.ReadAllText(pathFile).Trim();
        return host.Length > 0 && File.Exists(host) ? host : null;
    }

    /// <summary>
    ///     Maps <c>{prefix}/_framework/debug</c> and <c>{prefix}/_framework/debug/ws-proxy</c> onto the proxy at
    ///     <paramref name="hostDll" />.
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints, string prefix, string hostDll)
    {
        var launcher = new Launcher(hostDll);
        endpoints.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(launcher.Dispose);

        var root = prefix.TrimEnd('/') + "/" + DebugPath;

        endpoints.MapGet(root, static context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync(
                "The WebAssembly debug proxy is available. Start a debugging session from your editor: its launch "
                + "configuration's inspectUri points the browser debugger at " + DebugPath + "/ws-proxy.\n");
        });

        endpoints.MapGet(root + "/ws-proxy", async context =>
        {
            if (!TryParseBrowser(context.Request.Query["browser"], out var browser, out var devTools))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(
                    "ws-proxy needs ?browser= naming the browser's DevTools socket on this machine "
                    + "(ws://127.0.0.1:<port>/devtools/browser/<id>).").ConfigureAwait(false);
                return;
            }

            var proxy = await launcher.EnsureStartedAsync(devTools, context.RequestAborted).ConfigureAwait(false);
            context.Response.Redirect(RedirectTarget(proxy, browser));
        });
    }

    /// <summary>
    ///     Reads <c>?browser=</c>: a <c>ws</c>/<c>wss</c> URL on a loopback host. <paramref name="devTools" /> is the
    ///     DevTools HTTP origin the proxy connects to.
    /// </summary>
    internal static bool TryParseBrowser(string? value, out Uri browser, out string devTools)
    {
        browser = null!;
        devTools = "";
        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("ws" or "wss")
            || !parsed.IsLoopback)
        {
            return false;
        }

        browser = parsed;
        devTools = $"http://{parsed.Authority}";
        return true;
    }

    /// <summary>The debugger's socket, moved onto the proxy: its origin, the browser socket's path and query.</summary>
    internal static string RedirectTarget(Uri proxy, Uri browser) =>
        $"ws://{proxy.Authority}{browser.PathAndQuery}";

    /// <summary>The address on the proxy's <c>Now listening on:</c> line, or null for any other line.</summary>
    internal static Uri? ListeningAddress(string? line) =>
        line is not null && ListeningLine().Match(line) is { Success: true } match
        && Uri.TryCreate(match.Groups["url"].Value.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;

    /// <summary>How the proxy is started: the running <c>dotnet</c>, bound to loopback on a free port.</summary>
    internal static ProcessStartInfo StartInfo(string dotnet, string hostDll, int ownerPid, string devTools)
    {
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in (string[])["exec", hostDll, "--OwnerPid", ownerPid.ToString(System.Globalization.CultureInfo.InvariantCulture), "--DevToolsUrl", devTools])
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";

        // The app's own environment is not the proxy's: a Development here would turn on its developer pages.
        start.Environment.Remove("ASPNETCORE_ENVIRONMENT");
        start.Environment.Remove("DOTNET_ENVIRONMENT");
        return start;
    }

    [GeneratedRegex(@"Now listening on:\s*(?<url>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex ListeningLine();

    /// <summary>One proxy per DevTools endpoint, started on first use and stopped with the host.</summary>
    private sealed class Launcher(string hostDll) : IDisposable
    {
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        private readonly ConcurrentDictionary<string, Lazy<Task<(Process Process, Uri Address)>>> _proxies = new(StringComparer.Ordinal);

        public async Task<Uri> EnsureStartedAsync(string devTools, CancellationToken cancellationToken)
        {
            // A browser restarted by a new debug session listens on a new port, so each endpoint gets its own proxy.
            var started = _proxies.GetOrAdd(devTools, key => new Lazy<Task<(Process, Uri)>>(() => StartAsync(key)));
            try
            {
                return (await started.Value.WaitAsync(cancellationToken).ConfigureAwait(false)).Address;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A failed start is not cached: the next attempt tries again.
                _proxies.TryRemove(new KeyValuePair<string, Lazy<Task<(Process, Uri)>>>(devTools, started));
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var proxy in _proxies.Values)
            {
                if (proxy.IsValueCreated && proxy.Value.IsCompletedSuccessfully)
                {
                    try
                    {
                        proxy.Value.Result.Process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Already gone: --OwnerPid ends it with the host anyway.
                    }

                    proxy.Value.Result.Process.Dispose();
                }
            }
        }

        private async Task<(Process, Uri)> StartAsync(string devTools)
        {
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } hostPath ? hostPath : "dotnet";
            var process = Process.Start(StartInfo(dotnet, hostDll, Environment.ProcessId, devTools))
                ?? throw new InvalidOperationException($"Could not start the WebAssembly debug proxy ({hostDll}).");

            using var timeout = new CancellationTokenSource(StartTimeout);
            try
            {
                while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                {
                    if (ListeningAddress(line) is { } address)
                    {
                        // Keep draining, or a full pipe would block the proxy's own logging.
                        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);
                        return (process, address);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            var error = process.HasExited ? await process.StandardError.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false) : "";
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
            throw new InvalidOperationException(
                $"The WebAssembly debug proxy ({hostDll}) did not report an address within {StartTimeout.TotalSeconds:0} s. {error}".Trim());
        }
    }
}
