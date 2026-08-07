namespace Rask.Core.Live;

/// <summary>
///     How <c>LiveSession</c> / <c>WasmLiveSession</c> picks the wire
///     payload shape on each render.
/// </summary>
public enum LiveDiffMode
{
    /// <summary>
    ///     Always ship the full rendered HTML — the behaviour Rask had
    ///     before the diff codec landed. Default; safe; matches existing tests and the
    ///     existing client morph path.
    /// </summary>
    DisabledFull = 0,

    /// <summary>
    ///     Ship a <see cref="LivePayload.BuildPayloadUtf8Diff" /> payload
    ///     whenever a diff is computable and client-applicable. Falls back to full HTML
    ///     only when the diff would be <em>larger</em> than re-sending the body, on the
    ///     first render (no baseline), on structural ops the client interpreter can't
    ///     apply without morph-quality book-keeping (positional
    ///     <see cref="EditOpKind.InsertSubtree" /> / <see cref="EditOpKind.RemoveSubtree" />
    ///     / <see cref="EditOpKind.MoveSubtree" /> — keyed list edits are diffed), and on
    ///     out-of-band side effects (auth, download, jsInvokes, navigation). This is the
    ///     default: any genuine in-place state change ships as a diff regardless of page
    ///     size.
    /// </summary>
    Auto = 1,

    /// <summary>
    ///     Always ship a diff payload when one is computable,
    ///     <em>
    ///         even when it
    ///         would be larger than the full HTML
    ///     </em>
    ///     . Mostly useful for tests and
    ///     benchmarks that want to lock in the diff path regardless of byte size.
    ///     Otherwise identical to <see cref="Auto" /> — still falls back to full HTML on
    ///     the first render, on structural ops, and on out-of-band side effects (auth,
    ///     download) that the diff wire format doesn't yet carry.
    /// </summary>
    Forced = 2
}

/// <summary>
///     Per-app live-runtime options exposed through
///     <c>services.AddRask(o => o.DiffMode = ...)</c>. Defaults are tuned for the
///     "byte-savings out of the box" experience: <see cref="DiffMode" /> is
///     <see cref="LiveDiffMode.Auto" /> so a fresh app sees counter updates and
///     similar in-place state changes ship as a handful of bytes instead of the
///     whole rendered body. Override to <see cref="LiveDiffMode.DisabledFull" />
///     for bit-for-bit pre-codec behaviour, or to <see cref="LiveDiffMode.Forced" />
///     for testing the diff path unconditionally.
/// </summary>
public sealed class RaskLiveOptions
{
    private string _pathBase = string.Empty;
    public LiveDiffMode DiffMode { get; set; } = LiveDiffMode.Auto;

    /// <summary>
    ///     Maximum number of concurrent live sessions the server will hold. Each session
    ///     pins a component tree and a DI scope, so an unbounded count is a memory-exhaustion
    ///     (DoS) surface for hosts exposed to untrusted traffic. <c>0</c> (default) means
    ///     unlimited, preserving prior behaviour. When set, a GET that would create a session
    ///     beyond the cap is answered with <c>503 Service Unavailable</c> + <c>Retry-After</c>;
    ///     existing sessions and auth challenge/forbid redirects are unaffected. This is a
    ///     coarse backstop — pair it with a reverse-proxy rate limit for precise control.
    ///     Sessions are reclaimed shortly after their socket disconnects (the grace-period
    ///     removal), so the live count tracks active clients, not cumulative visits.
    /// </summary>
    public int MaxSessions { get; set; }

    /// <summary>
    ///     Whether the scoped-CSS bundle is minified (comments + insignificant whitespace stripped) before
    ///     it is hashed and served. <c>null</c> (default) means <b>auto</b>: on outside the Development
    ///     environment, off in Development so hot-reloaded CSS stays readable — resolved by
    ///     <c>UseRask</c> from <c>IHostEnvironment</c>. Set <c>true</c>/<c>false</c> to force it. Minifying
    ///     before hashing keeps the digest, immutable URL, and brotli/gzip caches all keyed off the
    ///     minified bytes. Only the CSS bundle is minified; the JS bundle is served as-is.
    /// </summary>
    public bool? MinifyScopedAssets { get; set; }

    /// <summary>
    ///     Per-app URL prefix. Empty (default) keeps every framework URL at the
    ///     origin root. A non-empty value like <c>"/appA"</c> scopes every emitted
    ///     framework URL (head asset links, runtime script src, WS connect, upload /
    ///     download / auth endpoints) AND every server-side endpoint registration
    ///     under that prefix, so two Rask apps can live side-by-side on one origin.
    ///     Normalized at assignment to leading slash + no trailing slash: <c>"/"</c>
    ///     and <c>""</c> collapse to <c>""</c>; <c>"appA"</c> and <c>"/appA/"</c>
    ///     both become <c>"/appA"</c>.
    /// </summary>
    public string PathBase
    {
        get => _pathBase;
        set => _pathBase = RaskPath.Normalize(value);
    }
}

/// <summary>
///     Static accessor for the process-wide live options that back the content-addressed asset
///     registries. Set by <c>AddRask()</c> / <c>UseRask&lt;TApp&gt;()</c> from the configured
///     <see cref="RaskLiveOptions" />. <see cref="PathBase" /> and <see cref="MinifyScopedAssets" />
///     live here because the <see cref="ScopedAssets.ScopedAssetRegistry" /> and
///     <c>HeadAssetRegistry</c> build one shared, content-hashed bundle per process — not per session.
///     The per-render <c>DiffMode</c>, by contrast, is carried on each <c>LiveSession</c>
///     (see <see cref="LiveSessionBase" />) so concurrent hosts and parallel tests don't race a
///     shared mutable field. Hosts that don't go through <c>AddRask()</c> (some standalone WASM
///     bootstraps) can also write these properties directly.
/// </summary>
public static class LiveOptions
{
    private static string _pathBase = string.Empty;

    /// <summary>
    ///     Resolved scoped-CSS minification switch read by <see cref="ScopedAssets.ScopedAssetRegistry" />
    ///     when it builds the bundle. <c>null</c> (default) = unresolved/off — <c>UseRask</c> resolves the
    ///     auto default from <see cref="RaskLiveOptions.MinifyScopedAssets" /> + the host environment; a
    ///     standalone host (or a test) can also set it directly.
    /// </summary>
    public static bool? MinifyScopedAssets { get; set; }

    /// <summary>
    ///     Whether the app is running in Development, as decided by the <em>host</em>. <c>null</c>
    ///     (default) = unresolved, in which case <see cref="Components.DefaultErrorPage" /> falls back to
    ///     reading the standard ASP.NET environment variables itself.
    /// </summary>
    /// <remarks>
    ///     This decides whether an error page shows a stack trace and a source excerpt or just a type and
    ///     a message, so getting it wrong is expensive in exactly the moment it matters. Core cannot ask
    ///     the host directly — it takes no dependency on <c>Microsoft.Extensions.Hosting</c>, deliberately
    ///     — and the environment-variable fallback it used instead is only correct when the environment
    ///     arrived that way. <c>dotnet run --environment Development</c>, <c>appsettings.json</c>,
    ///     assigning <c>builder.Environment.EnvironmentName</c>, and IDE profiles that set configuration
    ///     rather than the process environment all select Development without setting a variable, and all
    ///     of them silently produced the production error page while developing (#605). <c>UseRask</c>
    ///     now resolves this from <c>IWebHostEnvironment</c>; a standalone host or a test can set it
    ///     directly. Host-wide rather than per-session, like <see cref="MinifyScopedAssets" />.
    /// </remarks>
    public static bool? IsDevelopment { get; set; }

    /// <summary>
    ///     Active URL prefix (see <see cref="RaskLiveOptions.PathBase" />).
    ///     Always normalized: <c>""</c> or <c>"/segment"</c> (no trailing slash).
    /// </summary>
    public static string PathBase
    {
        get => _pathBase;
        set => _pathBase = RaskPath.Normalize(value);
    }
}

/// <summary>
///     Helpers for the <see cref="RaskLiveOptions.PathBase" /> string. Normalize
///     to <c>""</c> (root, default) or <c>"/segment"</c> (leading slash, no
///     trailing slash). Multi-segment values like <c>"/a/b"</c> are preserved.
/// </summary>
public static class RaskPath
{
    /// <summary>
    ///     Returns <c>""</c> if the input is null/empty/"/"/whitespace; otherwise
    ///     returns the input with leading slash ensured and trailing slash stripped.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim();
        if (s == "/")
        {
            return string.Empty;
        }

        if (s[0] != '/')
        {
            s = "/" + s;
        }

        while (s.Length > 1 && s[^1] == '/')
        {
            s = s[..^1];
        }

        return s;
    }
}
