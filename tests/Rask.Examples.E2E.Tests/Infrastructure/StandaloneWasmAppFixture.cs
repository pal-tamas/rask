using System.Net;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves the <em>published</em> <c>Rask.Example.Wasm</c> AppBundle from a tiny in-process
///     static-file server — the "any static host" (GitHub Pages) scenario, with no Rask runtime in
///     front of it. Publishing emits a complete <c>index.html</c> (populated SDK import map) plus the
///     fingerprinted, prebuilt .NET-WASM runtime (<c>-p:WasmBuildNative=false</c> skips the relink,
///     which is flaky across build environments); a plain file server then serves those bytes exactly
///     as a CDN/Pages host would.
///     <para>
///         This replaces the earlier WasmAppHost dev launcher: WasmAppHost resolves the import-map
///         index.html at request time from the build's static-web-assets manifest, and that resolution
///         is unreliable across SDK/build environments (it can serve a 0-byte body for the index route,
///         so the runtime never boots). Serving the published output removes that variable entirely
///         while still exercising the same thing this shard cares about — the WASM app booting under a
///         non-Rask static host.
///     </para>
///     <para>A non-file GET falls back to <c>index.html</c> so client-side deep links resolve.</para>
/// </summary>
public sealed class StandaloneWasmAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string ProjectRelativePath = "samples/Rask.Example.Wasm";
    private const int Port = 5096;

    // Content types the .NET-WASM boot needs served correctly — most importantly application/wasm and
    // application/json (blazor.boot config), and text/javascript for the ES module imports.
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".js"] = "text/javascript; charset=utf-8",
            [".mjs"] = "text/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".wasm"] = "application/wasm",
            [".json"] = "application/json; charset=utf-8",
            [".map"] = "application/json; charset=utf-8",
            [".dll"] = "application/octet-stream",
            [".pdb"] = "application/octet-stream",
            [".dat"] = "application/octet-stream",
            [".blat"] = "application/octet-stream",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".ico"] = "image/x-icon",
            [".webmanifest"] = "application/manifest+json"
        };

    private CancellationTokenSource? _cts;
    private HttpListener? _listener;
    private string _wwwroot = string.Empty;

    public string BaseUrl => $"http://localhost:{Port}";

    public string ServerLog => $"in-process static-file host over published wwwroot: {_wwwroot}";

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepoRoot();

        // Serve the Rask.Example.Wasm AppBundle the main test-suite build already published — via
        // Rask.Example.Wasm.Host's nested publish, with -p:WasmBuildNative=false so the prebuilt
        // .NET-WASM runtime is present (the native relink is flaky in some build environments). This
        // fixture is deliberately build-free: publishing in-fixture would add a concurrent MSBuild
        // under the full parallel suite and crash a worker node. It relies on the published output the
        // same way SubPathWasmAppFixture does.
        _wwwroot = Path.Combine(repoRoot, ProjectRelativePath, "bin", Configuration,
            "net10.0-browser", "publish", "wwwroot");
        if (!File.Exists(Path.Combine(_wwwroot, "index.html")))
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} not found at '{_wwwroot}'. This fixture relies on the " +
                "main test-suite build having published it (Rask.Example.Wasm.Host's nested publish). " +
                "Build the solution first — e.g. `dotnet build Rask.slnx -p:WasmBuildNative=false`.");
        }

        VerifyScopedBundleBaked();

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(() => ServeLoopAsync(_listener, _wwwroot, _cts.Token));

        await WaitForReadyAsync(TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        try { _cts?.Cancel(); }
        catch
        {
            /* ignore */
        }

        try { _listener?.Stop(); }
        catch
        {
            /* ignore */
        }

        _listener?.Close();
        _cts?.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>
    ///     Fail fast (and descriptively) if the published bundle is missing its baked scoped-JS bundle.
    ///     The single concatenated scoped-JS bundle is written by <c>BakeScopedAssetsTask</c> into the
    ///     published <c>wwwroot/_rask/a/{hash}.js</c>; without it every CodeSample page 404s on
    ///     <c>window.Rask.CodeSample</c> and highlighting never runs — which would otherwise surface as
    ///     several ~5s "locator never visible" timeouts with no hint at the cause.
    /// </summary>
    private void VerifyScopedBundleBaked()
    {
        var scopedDir = Path.Combine(_wwwroot, "_rask", "a");
        var jsFile = Directory.Exists(scopedDir)
            ? Directory.EnumerateFiles(scopedDir, "*.js").FirstOrDefault()
            : null;
        if (jsFile is null)
        {
            throw new InvalidOperationException(
                $"Published {ProjectRelativePath} is missing its baked scoped-JS bundle under '{scopedDir}'. " +
                "BakeScopedAssetsTask did not emit /_rask/a/*.js — standalone WASM would 404 on the scoped " +
                "bundle and highlight/JS-interop would time out. See Rask.Wasm/build/Rask.Wasm.targets.");
        }
    }

    private static async Task ServeLoopAsync(HttpListener listener, string wwwroot, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                break; // listener stopped/disposed
            }

            _ = Task.Run(() => HandleRequestAsync(context, wwwroot), ct);
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext ctx, string wwwroot)
    {
        try
        {
            var rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
            if (rel.Length == 0)
            {
                rel = "index.html";
            }

            // Defense-in-depth before any filesystem access: reject an absolute path or any '..' segment
            // so a crafted request URL can't escape the served wwwroot (CodeQL: uncontrolled data used
            // in path expression). The canonicalized-prefix check below is the second barrier.
            if (Path.IsPathRooted(rel) || rel.Contains("..", StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            var root = Path.GetFullPath(wwwroot);
            var path = Path.GetFullPath(Path.Combine(root, rel));
            var rooted = path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                         || path.Equals(root, StringComparison.Ordinal);

            if (!rooted || !File.Exists(path))
            {
                // SPA fallback: a non-file route (no extension) serves index.html; a missing file 404s.
                if (rooted && !Path.HasExtension(rel))
                {
                    path = Path.Combine(wwwroot, "index.html");
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
            }

            ctx.Response.ContentType = ContentTypes.TryGetValue(Path.GetExtension(path), out var type)
                ? type
                : "application/octet-stream";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            var bytes = await File.ReadAllBytesAsync(path);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch
        {
            try { ctx.Response.Abort(); }
            catch
            {
                /* client gone */
            }
        }
    }

    private async Task WaitForReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var resp = await http.GetAsync($"{BaseUrl}/index.html");
                if ((int)resp.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                /* not yet listening */
            }
            catch (TaskCanceledException)
            {
                /* per-request timeout */
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Static WASM host did not respond on {BaseUrl} within {timeout}.");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
