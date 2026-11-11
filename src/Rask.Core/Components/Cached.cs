using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Caching;
using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     Draws what it wraps once and serves that markup to every later request that would draw the same:
///     <c>Cached.Name("sales-summary").For(10.Minutes)[SalesSummary]</c>. ASP.NET's <c>&lt;cache&gt;</c> and
///     Blazor's <c>CacheView</c>, for a Rask page.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it keeps is markup, so what it wraps must be static.</b> A button, a bound input, an element
///         reference, a lazy region or a head contribution inside would be served to the next visitor dead — nothing
///         would be listening — so the first render that would store one throws instead, naming it, and
///         <c>RASK117</c> reports the plain cases at compile time.
///     </para>
///     <para>
///         <b>A hit mounts nothing.</b> The components it wraps are built with the page but held back, as
///         <see cref="Lazy" />'s are: their <c>OnMount</c> and the reads it starts never run, which is the work it
///         saves. So put the expensive part — the query, the aggregate — inside a component it wraps, not in the
///         markup of the page that writes it.
///     </para>
///     <para>
///         <b>A miss draws them live</b> and stores the markup once their work has settled — on the first
///         <c>GET</c>, after the waves that wait for their data; in a live session, on the first render after it lands.
///         The session that drew them keeps them live; every later one is handed the markup.
///     </para>
///     <para>
///         <b>Who sees whose markup.</b> The key always holds the tenant and the visitor's culture, and the signed-in
///         user unless the fragment says <see cref="AcrossUsers" />. When a user cannot be told apart, or a header or
///         route it varies by cannot be read, it is drawn live and never stored.
///     </para>
///     <para>
///         <b>In a live page it is an opaque island:</b> the stored markup is one verbatim frame, which the diff
///         leaves alone while it is unchanged. The page keeps what it drew until the key changes — a query value it
///         varies by, a sign-in — and then looks again.
///     </para>
/// </remarks>
public sealed class Cached : Component
{
    private CachedDraw _draw;
    private string? _key;
    private Raw? _stored;
    private IFragmentStore? _store;
    private bool _lookedOnce;
    private bool _kept;
    private int _outstanding;
    private int _startedThisWalk;

    // The lookup that had not landed when the page was built: the first render waits for it, as for any hook.
    private Task? _lookup;

    // The fragments whose walk is running on this thread, innermost first: work a component under one starts
    // is work that fragment, and every fragment around it, must wait for before storing anything.
    [ThreadStatic] private static Cached? _walking;
    private Cached? _outer;

    /// <summary>What it is stored under: one name per fragment, shared by every request that draws it.</summary>
    public required string Name { get; set; }

    /// <summary>How long it is kept after it is drawn: <c>.For(10.Minutes)</c>. Five minutes by default (<see cref="CachedOptions.DefaultLifetime" />).</summary>
    public TimeSpan? For { get; set; }

    /// <summary>How long it may go unread before it is dropped, within <see cref="For" />: <c>.Sliding(2.Minutes)</c>.</summary>
    public TimeSpan? Sliding { get; set; }

    /// <summary>Query values it varies by: <c>.VaryByQuery("status", "page")</c>.</summary>
    public string[]? VaryByQuery { get; set; }

    /// <summary>Varies by the page's path, and so by every value in its route: <c>.VaryByRoute()</c>.</summary>
#pragma warning disable CA1805 // the initializer is what keeps VaryByRoute an optional chain step (no initializer = required)
    public bool VaryByRoute { get; set; } = false;
#pragma warning restore CA1805

    /// <summary>Request headers it varies by: <c>.VaryByHeader("User-Agent")</c>. Drawn live where there is no request to read — in the browser.</summary>
    public string[]? VaryByHeader { get; set; }

    /// <summary>A value of the app's own it varies by — an id, a version: <c>.VaryBy(product.Id)</c>.</summary>
    public object? VaryBy { get; set; }

    /// <summary>
    ///     One copy for every user of a tenant, rather than one per user: <c>.AcrossUsers()</c>. Only for markup
    ///     that says nothing about who is looking. The tenant is still in the key.
    /// </summary>
#pragma warning disable CA1805 // the initializer is what keeps AcrossUsers an optional chain step (no initializer = required)
    public bool AcrossUsers { get; set; } = false;
#pragma warning restore CA1805

    /// <summary>Tags to forget it by, besides its <see cref="Name" />: <c>.Tags("catalog")</c>, then <c>await CachedFragments.Forget("catalog")</c>.</summary>
    public string[]? Tags { get; set; }

    /// <summary>Transparent: it draws what it wraps, or the markup that stood for it.</summary>
    protected override string? TagName => null;

    // Being walked for the first time with its key decided, which is when its markup is worth keeping.
    internal bool Capturing => _draw == CachedDraw.Live && !_kept;

    /// <summary>What it wraps: drawn and stored on a miss, never mounted on a hit.</summary>
    /// <param name="children">The wrapped components.</param>
    public new Cached this[params Component?[] children]
    {
        get
        {
            Children = children;
            var services = LiveRenderContext.Current?.Services;
            var key = FragmentKeys.For(this, services);
            if (!_lookedOnce || !string.Equals(key, _key, StringComparison.Ordinal))
            {
                Look(key, services);
            }

            // Held even on a miss: what it wraps is mounted by its own walk, where the work it starts is seen.
            Hold(children, _draw != CachedDraw.Uncached);
            return this;
        }
    }

    /// <inheritdoc />
    protected override Task OnMount() => _lookup ?? Task.CompletedTask;

    /// <inheritdoc />
    protected override Component? Render()
    {
        switch (_draw)
        {
            case CachedDraw.Stored:
                return _stored;
            case CachedDraw.Live or CachedDraw.Uncached:
                Hold(Children, held: false);
                return new Fragment { Children = Children ?? [] };
            default:
                return null;
        }
    }

    private void Look(string? key, IServiceProvider? services)
    {
        _lookedOnce = true;
        _key = key;
        _kept = false;
        _stored = null;
        _lookup = null;
        MarkDirtyForFrame();

        if (key is null)
        {
            _draw = CachedDraw.Uncached;
            return;
        }

        _store = services?.GetService<IFragmentStore>() ?? MemoryFragmentStore.Shared;
        var found = _store.Get(key);
        if (found.IsCompletedSuccessfully)
        {
            Land(key, found.Result);
            return;
        }

        _draw = CachedDraw.Undecided;
        _lookup = Landing(found, key);
        if (HasInitializedInternal)
        {
            // Already in the page: the hook that waits is this one, run the way a script's callback is, rendering
            // when it lands. Before the mount, OnMount hands the same task to the lifecycle.
            var lookup = _lookup;
            _ = RunFromScriptNow(() => lookup);
        }
    }

    private async Task Landing(ValueTask<string?> found, string key)
    {
        string? html;
        try
        {
            html = await found.ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A cache that cannot be read must never take the page down: the fragment is drawn live.
        catch (Exception)
#pragma warning restore CA1031
        {
            html = null;
        }

        Land(key, html);
    }

    private void Land(string key, string? html)
    {
        if (!string.Equals(key, _key, StringComparison.Ordinal))
        {
            return;
        }

        _draw = html is null ? CachedDraw.Live : CachedDraw.Stored;
        _stored = html is null ? null : new Raw(html);
        MarkDirtyForFrame();
    }

    // What a lazy does to what it wraps: built with the page, mounted only by the walk that draws it.
    private static void Hold(IEnumerable<Component?>? children, bool held)
    {
        if (children is null)
        {
            return;
        }

        foreach (var child in children)
        {
            if (child is null)
            {
                continue;
            }

            child.HoldBackInternal(held);
            Hold(child.Children, held);
        }
    }

    /// <summary>Opens <paramref name="fragment" />'s walk on the thread: work started under it from here on is counted against it.</summary>
    internal static void EnterWalk(Cached fragment)
    {
        fragment._outer = _walking;
        fragment._startedThisWalk = 0;
        _walking = fragment;
    }

    /// <summary>Closes what <see cref="EnterWalk" /> opened.</summary>
    internal static void LeaveWalk(Cached fragment)
    {
        _walking = fragment._outer;
        fragment._outer = null;
    }

    /// <summary>
    ///     A lifecycle hook under every fragment being walked here started work it has not finished: none of them
    ///     stores its markup until it has, because what they drew is its placeholder.
    /// </summary>
    internal static void NoteWork(Task painted)
    {
        for (var fragment = _walking; fragment is not null; fragment = fragment._outer)
        {
            fragment._startedThisWalk++;
            Interlocked.Increment(ref fragment._outstanding);
            _ = fragment.Settle(painted);
        }
    }

    // Counts the work off once it has painted, however it ended: a faulted hook is its boundary's to report.
    private async Task Settle(Task painted)
    {
        await painted.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Interlocked.Decrement(ref _outstanding);
    }

    /// <summary>
    ///     The markup its walk just wrote. Stored when everything under it has settled, after refusing what static
    ///     markup cannot carry.
    /// </summary>
    /// <param name="html">The markup.</param>
    /// <param name="addedHead">Whether something under it contributed to the page's head.</param>
    internal void Captured(string html, bool addedHead)
    {
        if (!Capturing || _startedThisWalk > 0 || Volatile.Read(ref _outstanding) > 0 || _key is not { } key || _store is not { } store)
        {
            return;
        }

        CachedRefusal.ThrowIfLive(Name, html, addedHead);
        _kept = true;

        var options = store.Options;
        var lifetime = new FragmentLifetime(For ?? options.DefaultLifetime, Sliding);
        _ = Keep(store, key, html, lifetime, TagsOf());
    }

    private string[] TagsOf()
    {
        var tags = new string[1 + (Tags?.Length ?? 0)];
        tags[0] = FragmentKeys.Tag(Name);
        for (var i = 1; i < tags.Length; i++)
        {
            tags[i] = FragmentKeys.Tag(Tags![i - 1]);
        }

        return tags;
    }

    // A cache that cannot be written costs the next request a render, never this one its page: nothing is stored.
    private static async Task Keep(IFragmentStore store, string key, string html, FragmentLifetime lifetime, string[] tags) =>
        await store.Set(key, html, lifetime, tags).AsTask().ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
}
