using System.Net;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Serves a <em>published</em> WASM AppBundle's <c>wwwroot</c> from a tiny in-process static-file host
///     — the "any static host" (GitHub Pages / CDN) scenario, with no Rask runtime in front of it. Shared
///     by every static-host fixture; a subclass only supplies its published-project path and a unique port
///     (and may hook <see cref="OnBundleLocated" /> for extra checks). Build-free: it relies on the bundle
///     having been published to the default output path first, so publishing in-fixture (a concurrent
///     MSBuild under the full parallel suite) is avoided.
///     <para>A non-file GET falls back to <c>index.html</c> so client-side deep links resolve.</para>
/// </summary>
public abstract class StaticWwwrootHostFixture : IAsyncLifetime
{
#if DEBUG
    protected const string Configuration = "Debug";
#else
    protected const string Configuration = "Release";
#endif

    // Content types the .NET-WASM boot needs served correctly — most importantly application/wasm and
    // application/json (boot config), and text/javascript for the ES module imports.
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

    /// <summary>Repo-relative path of the WASM project whose published wwwroot is served.</summary>
    protected abstract string ProjectRelativePath { get; }

    /// <summary>
    ///     A port unique to this fixture — the static-host collections run in parallel, so a shared port
    ///     would bind-clash and crash both hosts.
    /// </summary>
    protected abstract int Port { get; }

    protected string Wwwroot { get; private set; } = string.Empty;

    public string BaseUrl => $"http://localhost:{Port}";

    public string ServerLog => $"in-process static-file host over published wwwroot: {Wwwroot}";

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepoRoot();
        Wwwroot = Path.Combine(repoRoot, ProjectRelativePath, "bin", Configuration,
            "net10.0-browser", "publish", "wwwroot");
        if (!File.Exists(Path.Combine(Wwwroot, "index.html")))
        {
            throw new InvalidOperationException(MissingBundleMessage(Wwwroot));
        }

        OnBundleLocated(Wwwroot);

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(() => ServeLoopAsync(_listener, Wwwroot, _cts.Token));

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

    /// <summary>The message thrown when the published bundle is missing — subclasses tailor the fix hint.</summary>
    protected virtual string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. Build/publish it (in {Configuration}) first.";

    /// <summary>Hook for extra validation once the bundle directory is resolved (e.g. baked-asset checks).</summary>
    protected virtual void OnBundleLocated(string wwwroot)
    {
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

            // Reject absolute paths / '..' before any filesystem access (path-traversal guard); the
            // canonicalized-prefix check below is the second barrier.
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

        throw new TimeoutException($"Static host did not respond on {BaseUrl} within {timeout}.");
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
