using System.Net;

namespace Rask.Site.E2E.Tests.Infrastructure;

/// <summary>
///     Serves a <em>published</em> WASM AppBundle's <c>wwwroot</c> from a tiny in-process static-file host
///     — the "any static host" (GitHub Pages / CDN) scenario, with no Rask runtime in front of it. Shared
///     by every static-host fixture; a subclass only supplies its published-project path (and may hook
///     <see cref="OnBundleLocated" /> for extra checks) — the port is assigned by the OS, so nothing has to
///     be kept unique by hand and two runs cannot collide. Build-free: it relies on the bundle
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
            [".webmanifest"] = "application/manifest+json",
            // The files the publish writes for crawlers and AI assistants — sitemap.xml and robots.txt from
            // the prerender pass, llms.txt and the guides' Markdown twins from the site. GitHub Pages serves
            // them as these types.
            [".xml"] = "application/xml; charset=utf-8",
            [".txt"] = "text/plain; charset=utf-8",
            [".md"] = "text/markdown; charset=utf-8"
        };

    private readonly Lazy<Hosted> _hosted;

    protected StaticWwwrootHostFixture() =>
        _hosted = new Lazy<Hosted>(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Repo-relative path of the WASM project whose published wwwroot is served.</summary>
    protected abstract string ProjectRelativePath { get; }

    /// <summary>
    ///     The URL path the bundle is served under — <c>/demos/data</c> for an app published with that
    ///     <c>RaskPathBase</c>, the way a static host serves it beside another site. Empty serves it at the origin
    ///     root; outside a non-empty mount every request is a 404, as it would be on that host.
    /// </summary>
    protected virtual string MountPath => string.Empty;

    /// <summary>
    ///     The loopback port this fixture's host is listening on, assigned by the OS when the host starts —
    ///     see <see cref="BindEphemeral" /> for why it is not a constant.
    /// </summary>
    protected int Port => _hosted.Value.Port;

    protected string Wwwroot => _hosted.Value.Wwwroot;

    /// <summary>
    ///     The host's origin. The FIRST read starts it — finds the published bundle (or throws, saying how to
    ///     publish it) and binds the port — so a test that never asks for it never needs the bundle.
    /// </summary>
    /// <remarks>
    ///     Lazy because Rask.Site.E2E.Tests holds its host as an ASSEMBLY fixture, which xUnit creates before
    ///     any test in the assembly runs: started in <see cref="InitializeAsync" />, it failed every test in the
    ///     assembly — the convention tests the unit gate runs without publishing the site among them.
    /// </remarks>
    public string BaseUrl => $"http://localhost:{Port}";

    public string ServerLog => $"in-process static-file host over published wwwroot: {Wwwroot}";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (!_hosted.IsValueCreated)
        {
            return ValueTask.CompletedTask;
        }

        var hosted = _hosted.Value;
        try { hosted.Cts.Cancel(); }
        catch
        {
            /* ignore */
        }

        try { hosted.Listener.Stop(); }
        catch
        {
            /* ignore */
        }

        // Guarded like its siblings: xUnit reports a fixture-cleanup throw as a failed *run*, so an
        // unguarded teardown could fail a suite in which every test passed and name no test as the cause.
        try { hosted.Listener.Close(); }
        catch
        {
            /* ignore */
        }

        hosted.Cts.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     Locates the bundle and starts serving it. Synchronous on purpose: <c>HttpListener.Start()</c> has
    ///     bound the socket by the time it returns, so the host answers from then on and there is nothing to
    ///     await — which is what lets <see cref="BaseUrl" /> start it from a plain property read.
    /// </summary>
    private Hosted Start()
    {
        var wwwroot = Path.Combine(Repository.Root, ProjectRelativePath, "bin", Configuration,
            "net11.0-browser", "publish", "wwwroot");
        if (!File.Exists(Path.Combine(wwwroot, "index.html")))
        {
            throw new InvalidOperationException(MissingBundleMessage(wwwroot));
        }

        OnBundleLocated(wwwroot);

        var cts = new CancellationTokenSource();
        var (listener, port) = BindEphemeral(GetType().Name);
        var mount = MountPath.TrimEnd('/');
        _ = Task.Run(() => ServeLoopAsync(listener, wwwroot, mount, cts.Token));
        return new Hosted(listener, cts, port, wwwroot);
    }

    /// <summary>
    ///     Starts a listener on an OS-assigned loopback port, so two runs of the suite — or two worktrees
    ///     of this repo, which is the ordinary way to work here — cannot collide.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="LoopbackPort.Reserve" /> explains why the number comes from the OS rather than a
    ///         constant, and which loopback family the probe has to bind.
    ///     </para>
    ///     <para>
    ///         <c>HttpListener</c> rejects a <c>:0</c> prefix outright ("Invalid port in prefix") and cannot
    ///         report an assigned port back, so the reserved number is passed to it and
    ///         <c>HttpListener.Start()</c> is then the authoritative test. This host is in-process, so it can
    ///         afford that: the probe-release window costs another candidate rather than the run. (The
    ///         explicit <c>http://[::1]:port/</c> form that would let us bind both families is itself
    ///         rejected as an invalid prefix.)
    ///     </para>
    /// </remarks>
    private static (HttpListener Listener, int Port) BindEphemeral(string fixture, int attempts = 20)
    {
        HttpListenerException? last = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var port = LoopbackPort.Reserve();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException ex)
            {
                last = ex;
                listener.Close();
            }
        }

        throw new InvalidOperationException(
            $"{fixture} could not bind a loopback port for its static host: {attempts} OS-assigned " +
            $"candidates were all refused. Something on this machine is claiming ports as fast as they " +
            $"are handed out — check for stragglers from an earlier run ('lsof -nP -iTCP -sTCP:LISTEN').",
            last);
    }

    /// <summary>The message thrown when the published bundle is missing — subclasses tailor the fix hint.</summary>
    protected virtual string MissingBundleMessage(string wwwroot) =>
        $"Published {ProjectRelativePath} not found at '{wwwroot}'. Build/publish it (in {Configuration}) first.";

    /// <summary>Hook for extra validation once the bundle directory is resolved (e.g. baked-asset checks).</summary>
    protected virtual void OnBundleLocated(string wwwroot)
    {
    }

    private static async Task ServeLoopAsync(HttpListener listener, string wwwroot, string mount, CancellationToken ct)
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

            _ = Task.Run(() => HandleRequestAsync(context, wwwroot, mount), ct);
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext ctx, string wwwroot, string mount)
    {
        try
        {
            var requested = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);
            if (mount.Length > 0)
            {
                if (!requested.Equals(mount, StringComparison.Ordinal)
                    && !requested.StartsWith(mount + "/", StringComparison.Ordinal))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                requested = requested[mount.Length..];
            }

            var rel = requested.TrimStart('/');
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
                // DIRECTORY INDEX FIRST: the prerender pass writes a rendered page per route as
                // <route>/index.html, and production serves it (Rask.Spa.Hosting uses UseDefaultFiles
                // with DefaultFileNames = ["index.html"]). Skipping straight to the SPA fallback served
                // the BOOT SHELL instead, which made every journey a cold-WASM-boot race and left the
                // prerendered output with no browser coverage at all (#1034).
                var directoryIndex = rooted && !Path.HasExtension(rel)
                    ? Path.Combine(path, "index.html")
                    : null;

                if (directoryIndex is not null && File.Exists(directoryIndex))
                {
                    path = directoryIndex;
                }
                // SPA fallback: a non-file route (no extension) serves index.html; a missing file 404s.
                else if (rooted && !Path.HasExtension(rel))
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

    private sealed record Hosted(HttpListener Listener, CancellationTokenSource Cts, int Port, string Wwwroot);
}
