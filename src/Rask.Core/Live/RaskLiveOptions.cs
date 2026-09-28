namespace Rask.Core.Live;

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
    ///     <c>MapRask</c> from <c>IHostEnvironment</c>. Set <c>true</c>/<c>false</c> to force it. Minifying
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
