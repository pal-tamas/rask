using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;
using Rask.Server.Diagnostics;
using Rask.Server.Http;

namespace Rask.Server.Prerender;

/// <summary>
///     The public-page cache: stored copies of pages that need no sign-in, served to every visitor, and
///     rendered again in the background once they are older than <c>RenderModes.RevalidateAfter</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>A copy is never a visitor's render.</b> A request that finds no copy is served live, exactly as
///         it always was, and asks for one; <see cref="PageRevalidator" /> then renders the page for nobody —
///         no principal, no query, no request behind it — and that is what gets stored. What one visitor's
///         cookies, headers or session did to a page cannot reach the next visitor through here.
///     </para>
///     <para>
///         <b>Bounded by what the app declared.</b> Only planned paths are ever keys (<see cref="PlannedPaths" />),
///         each is rendered at most once at a time, and the whole cache sits under a byte budget. Traffic can
///         make it serve; it cannot make it grow.
///     </para>
///     <para>
///         <b>Reads are lock-free, changes are not.</b> A request reads a copy straight out of a concurrent
///         dictionary. Storing, evicting and pruning take one lock, because each has to agree with the others
///         about what the budget holds — and they happen a few times a minute, not per request.
///     </para>
///     <para>
///         Off unless the app built with <c>&lt;RaskPrerender&gt;true&lt;/RaskPrerender&gt;</c>, and always off in
///         Development, where every page keeps its session so an edit repaints.
///     </para>
/// </remarks>
internal sealed class PageCache
{
    private const string Category = "Rask.Prerender";
    private const string DocumentContentType = "text/html; charset=utf-8";

    // A render that could not be stored, or failed, is not asked for again on every request. At least this
    // long, and never less than the freshness bound — refreshing a refused page faster than a stored one
    // would be backwards.
    private static readonly TimeSpan MinimumRetry = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<PageCacheKey, PageCacheEntry> _entries = new();
    private readonly ConcurrentDictionary<PageCacheKey, long> _refusedUntil = new();
    private readonly ConcurrentDictionary<PageCacheKey, long> _retryAt = new();
    private readonly ConcurrentDictionary<PageCacheKey, byte> _pending = new();
    private readonly ConcurrentDictionary<IReadOnlyList<Type>, bool> _publicChains =
        new(ReferenceEqualityComparer.Instance);

    // Bounded, and written with TryWrite: a burst beyond it is simply not queued, and the next request for
    // that page asks again. Nothing waits on this queue, least of all a request.
    private readonly Channel<PageCacheKey> _requests = Channel.CreateBounded<PageCacheKey>(
        new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.Wait });

    private readonly TaskCompletionSource<RaskRootSelector> _attached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Lock _changes = new();
    private readonly RaskServerLimits _limits;
    private readonly TimeProvider _time;
    private readonly RaskMetrics? _metrics;
    private long _bytes;

    public PageCache(RaskServerLimits limits, TimeProvider time, RaskMetrics? metrics = null)
    {
        _limits = limits;
        _time = time;
        _metrics = metrics;

        if (limits.Prerender)
        {
            metrics?.TrackPrerenderBytes(() => Interlocked.Read(ref _bytes));
        }
    }

    /// <summary>
    ///     Whether pages are served from the cache: the app built with the switch, and this is not
    ///     Development.
    /// </summary>
    internal bool Enabled => _limits.Prerender && LiveOptions.IsDevelopment != true;

    /// <summary>The paths copies may be kept for.</summary>
    internal PlannedPaths Planned { get; } = new();

    /// <summary>The pages waiting to be rendered, read by <see cref="PageRevalidator" />.</summary>
    internal ChannelReader<PageCacheKey> Requests => _requests.Reader;

    /// <summary>Completes once <c>UseRask</c> has said which application owns which path.</summary>
    internal Task<RaskRootSelector> Attached => _attached.Task;

    /// <summary>How many copies are stored.</summary>
    internal int Count => _entries.Count;

    /// <summary>The bytes the stored copies take, compressed forms included.</summary>
    internal long Bytes => Interlocked.Read(ref _bytes);

    /// <summary>
    ///     Hands over the host's root selector. Called by <c>UseRask</c>; the first call wins, because a
    ///     host serves one application's pages.
    /// </summary>
    internal void Attach(RaskRootSelector selector) => _attached.TrySetResult(selector);

    /// <summary>
    ///     Answers <paramref name="context" /> from a stored copy when the request may be, and says whether it
    ///     did. A request that is not answered is served live by the caller, as it always was.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="path">The route path, <c>PathBase</c> removed.</param>
    /// <param name="chain">The resolved route chain.</param>
    /// <param name="resolvedToPage">Whether the path resolved to a page other than the not-found page.</param>
    /// <param name="culture">The negotiated language, or <c>null</c> when the app configured none.</param>
    /// <param name="user">The request's principal, after authentication.</param>
    /// <param name="selector">Which application owns which path.</param>
    internal async ValueTask<bool> TryServeAsync(
        HttpContext context,
        string path,
        IReadOnlyList<Type> chain,
        bool resolvedToPage,
        CultureNegotiation? culture,
        ClaimsPrincipal user,
        RaskRootSelector selector)
    {
        var request = context.Request;
        var bypass = PageCacheEligibility.Evaluate(
            getOrHead: HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method),
            hasQuery: request.QueryString.HasValue,
            mounted: selector.IsMounted(path),
            resolvedToPage: resolvedToPage,
            planned: Planned.Contains(path),
            cultureFromQuery: culture is { Source: CultureSource.Query },
            isPublic: resolvedToPage && IsPublic(chain));

        if (bypass != PageCacheBypass.None)
        {
            _metrics?.PrerenderBypassed(PageCacheEligibility.Tag(bypass));
            return false;
        }

        var key = PageCacheKey.For(path, culture);
        var now = _time.GetTimestamp();

        if (_refusedUntil.TryGetValue(key, out var refusedUntil) && now < refusedUntil)
        {
            _metrics?.PrerenderBypassed(PageCacheEligibility.Tag(PageCacheBypass.Refused));
            return false;
        }

        if (!_entries.TryGetValue(key, out var entry) || !LinksCurrentAssets(entry))
        {
            Request(key);
            _metrics?.PrerenderServed("miss");
            return false;
        }

        // A signed-in visitor is served the shared copy only when the page never asked who is signed in —
        // otherwise they would see the page as nobody. A request carrying credentials of its own is treated
        // the same way, whatever authentication made of them.
        if (entry.ReadsUser
            && (user.Identity?.IsAuthenticated == true || request.Headers.ContainsKey(HeaderNames.Authorization)))
        {
            _metrics?.PrerenderBypassed(PageCacheEligibility.Tag(PageCacheBypass.SignedInReadsUser));
            return false;
        }

        var stale = _limits.RevalidateAfter != Timeout.InfiniteTimeSpan
                    && _time.GetElapsedTime(entry.RenderedAt, now) >= _limits.RevalidateAfter;
        if (stale && (!_retryAt.TryGetValue(key, out var retryAt) || now >= retryAt))
        {
            Request(key);
        }

        _metrics?.PrerenderServed(stale ? "stale" : "hit");
        await WriteAsync(context, entry, localized: culture is not null).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    ///     Asks for <paramref name="key" /> to be rendered, unless it already is queued or rendering.
    /// </summary>
    internal void Request(PageCacheKey key)
    {
        if (!_pending.TryAdd(key, 0))
        {
            return;
        }

        if (!_requests.Writer.TryWrite(key))
        {
            _pending.TryRemove(key, out _);
        }
    }

    /// <summary>Marks <paramref name="key" />'s render finished, so the page may be asked for again.</summary>
    internal void Completed(PageCacheKey key) => _pending.TryRemove(key, out _);

    /// <summary>Whether requests for <paramref name="key" /> are being served live after a refused render.</summary>
    internal bool IsRefused(PageCacheKey key) =>
        _refusedUntil.TryGetValue(key, out var until) && _time.GetTimestamp() < until;

    /// <summary>
    ///     Applies a background render's verdict to the stored copy of <paramref name="key" />.
    /// </summary>
    /// <param name="key">The page rendered.</param>
    /// <param name="verdict">What <see cref="PageStorePolicy" /> decided.</param>
    /// <param name="document">The document to store, when the verdict is to store it.</param>
    /// <param name="readsUser">Whether the render read the signed-in user.</param>
    /// <param name="cssBundle">The scoped CSS bundle the document links to.</param>
    /// <param name="jsBundle">The scoped JS bundle the document links to.</param>
    /// <returns>The outcome, as the <c>rask.prerender.revalidations</c> tag.</returns>
    internal string Apply(
        PageCacheKey key,
        PageStoreVerdict verdict,
        string? document,
        bool readsUser,
        string cssBundle,
        string jsBundle)
    {
        if (verdict.Decision == PageStoreDecision.Store && document is not null)
        {
            return Store(key, document, readsUser, cssBundle, jsBundle);
        }

        lock (_changes)
        {
            if (verdict.Decision == PageStoreDecision.KeepStale && _entries.ContainsKey(key))
            {
                // A good page keeps serving through a bad render; it is asked for again later, not on the
                // very next request.
                _retryAt[key] = After(Retry);
                return "kept";
            }

            RemoveLocked(key);
            _refusedUntil[key] = After(verdict.Decision == PageStoreDecision.KeepStale ? Retry : Refusal);
            return verdict.Decision == PageStoreDecision.KeepStale ? "refused" : "evicted";
        }
    }

    /// <summary>
    ///     Drops the copies of paths the plan no longer holds — a page an <c>IPrerenderPaths</c> stopped
    ///     supplying is one the app no longer says exists.
    /// </summary>
    internal void PruneUnplanned()
    {
        lock (_changes)
        {
            foreach (var key in _entries.Keys)
            {
                if (!Planned.Contains(key.Path))
                {
                    RemoveLocked(key);
                }
            }

            foreach (var key in _refusedUntil.Keys)
            {
                if (!Planned.Contains(key.Path))
                {
                    _refusedUntil.TryRemove(key, out _);
                }
            }

            foreach (var key in _retryAt.Keys)
            {
                if (!Planned.Contains(key.Path))
                {
                    _retryAt.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    ///     Writes <paramref name="entry" /> as the response: the encoding the client accepts, an ETag that
    ///     turns a repeat visit into a 304, and headers that keep shared caches out.
    /// </summary>
    internal static Task WriteAsync(HttpContext context, PageCacheEntry entry, bool localized)
    {
        var response = context.Response;
        var policy = ShellCachePolicy.ForStoredCopy();
        response.Headers.CacheControl = policy.CacheControl;
        // Cookie, because whether this copy may be served is itself a function of the cookie: a signed-in
        // visitor on a page that reads the user is served live. Accept-Encoding, because the body depends on
        // it. Accept-Language only when the app negotiates one.
        response.Headers.Vary = localized
            ? "Accept-Encoding, " + policy.Vary + ", Accept-Language"
            : "Accept-Encoding, " + policy.Vary;

        var encoding = ScopedAssetCompression.Negotiate(context.Request.Headers.AcceptEncoding.ToString());
        var (body, tag) = encoding switch
        {
            "br" when entry.Brotli is { } brotli => (brotli, entry.BrotliTag!),
            "gzip" when entry.Gzip is { } gzip => (gzip, entry.GzipTag!),
            _ => (entry.Identity, entry.IdentityTag),
        };

        if (!ReferenceEquals(body, entry.Identity))
        {
            response.Headers.ContentEncoding = encoding;
        }

        // Results.Bytes answers If-None-Match and If-Modified-Since with a 304 and suppresses the body of a
        // HEAD, exactly as the scoped assets rely on it to.
        return Results.Bytes(body, DocumentContentType, lastModified: entry.ContentChangedAt, entityTag: tag)
            .ExecuteAsync(context);
    }

    private TimeSpan Retry =>
        _limits.RevalidateAfter == Timeout.InfiniteTimeSpan || _limits.RevalidateAfter < MinimumRetry
            ? MinimumRetry
            : _limits.RevalidateAfter;

    // A page whose render said it cannot be shared is not rendered again for a while: five minutes, or the
    // freshness bound when that is longer.
    private TimeSpan Refusal =>
        _limits.RevalidateAfter != Timeout.InfiniteTimeSpan && _limits.RevalidateAfter > TimeSpan.FromMinutes(5)
            ? _limits.RevalidateAfter
            : TimeSpan.FromMinutes(5);

    private string Store(PageCacheKey key, string document, bool readsUser, string cssBundle, string jsBundle)
    {
        // Built outside the lock: compressing a page is the expensive part, and nothing it reads can change
        // underneath — only this page's own render ever replaces its copy.
        _entries.TryGetValue(key, out var previous);
        var entry = PageCacheEntry.Create(
            document, readsUser, _time.GetTimestamp(), _time.GetUtcNow(), cssBundle, jsBundle, previous);

        lock (_changes)
        {
            // The plan moved while this rendered, and the page is no longer one the app says exists.
            if (!Planned.Contains(key.Path))
            {
                RemoveLocked(key);
                return "evicted";
            }

            // Measured against what is stored NOW, not what was there before the render: a prune or an evict
            // in between would otherwise be subtracted twice.
            _entries.TryGetValue(key, out var current);
            var delta = entry.Bytes - (current?.Bytes ?? 0);

            if (delta > 0 && Interlocked.Read(ref _bytes) + delta > _limits.PageCacheMaxBytes)
            {
                RaskDiagnostics.ReportOnce(
                    "prerender:budget",
                    RaskLogLevel.Warning,
                    Category,
                    () => $"The page cache is full ({_limits.PageCacheMaxBytes} bytes), so {key.Path} and pages "
                          + "after it cannot be stored as they are now. A page with a copy keeps serving it; a "
                          + "page without one is rendered for every request.");

                if (current is not null)
                {
                    // Still a good page, and already paid for. Serving it beats rendering live for every request
                    // while its bytes sit in the budget unused.
                    _retryAt[key] = After(Refusal);
                    return "kept";
                }

                _refusedUntil[key] = After(Refusal);
                return "refused";
            }

            var unchanged = current is not null && ReferenceEquals(entry.Identity, current.Identity);
            _entries[key] = entry;
            Interlocked.Add(ref _bytes, delta);
            _refusedUntil.TryRemove(key, out _);
            _retryAt.TryRemove(key, out _);
            return unchanged ? "unchanged" : "stored";
        }
    }

    private void RemoveLocked(PageCacheKey key)
    {
        if (_entries.TryRemove(key, out var removed))
        {
            Interlocked.Add(ref _bytes, -removed.Bytes);
        }

        _retryAt.TryRemove(key, out _);
    }

    // Saturates rather than overflowing: a RevalidateAfter of TimeSpan.MaxValue is a valid way to say "rarely",
    // and a timestamp that wrapped negative would say "now" — retrying on every request instead of never.
    private long After(TimeSpan span)
    {
        var now = _time.GetTimestamp();
        var ticks = span.TotalSeconds * _time.TimestampFrequency;
        return ticks >= long.MaxValue - now ? long.MaxValue : now + (long)ticks;
    }

    // Decided once per resolved chain — the route table hands back the same chain instance for a path every
    // time — from the attributes alone, with the guard's own walk.
    private bool IsPublic(IReadOnlyList<Type> chain) =>
        _publicChains.GetOrAdd(chain, static c => !RouteAuthorizationGuard.RequiresAuthorization(c));

    // A copy links the scoped CSS and JS bundles that existed when it was rendered. When a page rendered
    // since has added styles, the bundle's hash moves, and a copy still linking the old one would be served
    // without them — so it is treated as missing and rendered again against the current bundle.
    private static bool LinksCurrentAssets(PageCacheEntry entry) =>
        string.Equals(entry.CssBundle, ScopedAssetRegistry.GetBundleHash(AssetKind.Css), StringComparison.Ordinal)
        && string.Equals(entry.JsBundle, ScopedAssetRegistry.GetBundleHash(AssetKind.Js), StringComparison.Ordinal);
}
