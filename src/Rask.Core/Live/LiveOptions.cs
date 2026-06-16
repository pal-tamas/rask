namespace Rask.Core.Live;

/// <summary>
///     How <see cref="LiveSession" /> / <see cref="WasmLiveSession" /> picks the wire
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

    /// <summary>
    ///     Whether the framework eagerly preloads <em>every</em> registered scoped CSS and
    ///     JS asset into the page <c>&lt;head&gt;</c> on first render via non-blocking
    ///     <c>&lt;link rel="preload" fetchpriority="low"&gt;</c> hints — not just the assets of
    ///     components mounted on the current route. <c>true</c> (default) warms the HTTP cache
    ///     up front so a later mount (client-side navigation, a conditional section) finds its
    ///     scoped stylesheet/script already loaded: the body swaps with no flash of unstyled
    ///     content and no first-interaction wait for the scoped JS namespace. Scoped CSS is
    ///     selector-rewritten to <c>[data-r-xxxx]</c>, so preloading an unmounted component's
    ///     styles has no visual effect until its elements exist. Set <c>false</c> to fetch each
    ///     scoped asset only when its component first mounts (smaller first-load payload, at the
    ///     cost of a brief navigation FOUC the first time each new component type appears).
    /// </summary>
    public bool PreloadScopedAssets { get; set; } = true;
}

/// <summary>
///     Static accessor for the active live options. Set by <c>AddRask()</c> /
///     <c>UseRask&lt;TApp&gt;()</c> from the configured <see cref="RaskLiveOptions" />;
///     the live-session runtime (server + WASM) reads from here on every render so
///     the option flow stays trivially fast — no DI lookup in the hot path. Hosts
///     that don't go through <c>AddRask()</c> (some standalone WASM bootstraps)
///     can also write these properties directly.
/// </summary>
public static class LiveOptions
{
    private static string _pathBase = string.Empty;
    public static LiveDiffMode DiffMode { get; set; } = LiveDiffMode.Auto;

    /// <summary>
    ///     Active URL prefix (see <see cref="RaskLiveOptions.PathBase" />).
    ///     Always normalized: <c>""</c> or <c>"/segment"</c> (no trailing slash).
    /// </summary>
    public static string PathBase
    {
        get => _pathBase;
        set => _pathBase = RaskPath.Normalize(value);
    }

    /// <summary>
    ///     Active eager-preload flag (see <see cref="RaskLiveOptions.PreloadScopedAssets" />).
    ///     Read by <c>HeadAssetRegistry.EmitMountedAssets</c> on every render. Defaults to
    ///     <c>true</c>.
    /// </summary>
    public static bool PreloadScopedAssets { get; set; } = true;
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
