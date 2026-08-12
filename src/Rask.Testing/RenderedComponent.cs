using System.Diagnostics;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Testing;

/// <summary>
///     A rendered component under test. <see cref="Html" /> is the current markup; invoke a handler
///     (<see cref="InvokeAsync(string, string?)" />, <see cref="ClickAsync(string?)" />) to simulate an event, which dispatches it
///     and re-renders, or call <see cref="Render" /> to re-render after mutating external state.
/// </summary>
public class RenderedComponent : IRenderHandle
{
    private readonly Component _root;
    private readonly IServiceProvider _services;

    // A render walk is single-threaded per session by contract. Here two threads can reach it: the test,
    // and the thread-pool continuation of an asynchronous lifecycle hook signalling through this handle.
    // Serialize them rather than letting a render land mid-walk.
    private readonly Lock _renderLock = new();
    private bool _renderRequested;

    // A hook that signals on every render would otherwise spin here. A real host has the same hazard and
    // the same answer: drain a bounded number, then let the next explicit render carry on.
    private const int MaxQueuedRenders = 8;

    // Not sealed so RenderedComponent<T> can add Instance; the ctor stays internal, so this type is still
    // only constructible — and only derivable — inside the package.
    internal RenderedComponent(Component root, IServiceProvider services)
    {
        _root = root;
        _services = services;

        // Before the first render: LiveRenderContext snapshots the root's handle when it begins, and
        // GetOrCreate/AdoptChild hand that same handle down to the component under test — so it renders
        // through a handle here the way it does under a live session, rather than through none at all.
        _root.RenderHandle = this;
        Html = Render();
    }

    /// <summary>The current rendered HTML, reflecting the component's state as of the last render.</summary>
    public string Html { get; private set; } = string.Empty;

    // The parsed view of Html, rebuilt lazily and only when the markup actually changed. Keyed on
    // reference rather than value: Render() always produces a fresh string, so a reference match means
    // "this is literally the markup we parsed", with no comparison over a page-sized string.
    private HtmlNode? _parsed;
    private string? _parsedFrom;

    /// <summary>Re-renders the component (e.g. after mutating state it reads) and refreshes <see cref="Html" />.</summary>
    public string Render()
    {
        lock (_renderLock)
        {
            _renderRequested = false;
            Html = _root.RenderAsLiveRoot(_services);

            // Drain renders the walk itself asked for — a lifecycle hook calling StateHasChanged
            // synchronously, which is legal and which a live session answers with one coalesced render
            // once the dispatch unwinds rather than by re-entering the walk in flight.
            for (var i = 0; i < MaxQueuedRenders && _renderRequested; i++)
            {
                _renderRequested = false;
                Html = _root.RenderAsLiveRoot(_services);
            }

            _renderRequested = false;
            return Html;
        }
    }

    /// <summary>
    ///     Re-renders until <paramref name="predicate" /> accepts the markup, then returns it — the way to
    ///     test a component that loads asynchronously. <c>OnMountAsync</c> completes on a thread-pool
    ///     continuation, so the markup it produces is not there when <see cref="Render" /> returns; this
    ///     waits for it instead of guessing with a fixed delay.
    ///     <code>
    ///     var page = RaskTest.Render(new OrdersPage(store), services);
    ///     await page.WaitForAsync(html => !html.Contains("Reading…"));
    ///     </code>
    /// </summary>
    /// <param name="predicate">Receives the current markup on every attempt.</param>
    /// <param name="timeout">How long to keep trying. Defaults to 5 seconds.</param>
    /// <exception cref="TimeoutException">
    ///     The predicate never accepted the markup. The message carries the last markup seen, so a failure
    ///     shows what the component actually rendered rather than only that it timed out.
    /// </exception>
    public async Task<string> WaitForAsync(Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var budget = timeout ?? TimeSpan.FromSeconds(5);
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            var html = Render();
            if (predicate(html))
            {
                return html;
            }

            if (Stopwatch.GetElapsedTime(startedAt) >= budget)
            {
                throw new TimeoutException(
                    $"The rendered markup did not satisfy the predicate within {budget}. Last render:{Environment.NewLine}{html}");
            }

            await Task.Delay(PollInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Re-renders until the markup contains <paramref name="expected" />, then returns it — the common
    ///     shape of <see cref="WaitForAsync(Func{string, bool}, TimeSpan?)" />.
    /// </summary>
    /// <exception cref="TimeoutException">The text never appeared; the message carries the last markup.</exception>
    public Task<string> WaitForAsync(string expected, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return WaitForAsync(html => html.Contains(expected, StringComparison.Ordinal), timeout);
    }

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(5);

    // The handle the component under test renders through. It records the request rather than rendering
    // on the spot, which is what a live session does too: inside a dispatch every StateHasChanged sets a
    // pending flag and ONE coalesced render runs at the end (LiveSession.RequestRenderInternalAsync's
    // InHandlerScope branch). Rendering inline instead would be wrong twice over — it re-enters a walk
    // already in progress when a hook signals synchronously, and it renders halfway through a multicast
    // event, before the later subscribers of the same event (a Router and its Outlet both listen to
    // RouteState.Changed) have updated the state that render would read.
    Task IRenderHandle.RequestRenderAsync()
    {
        lock (_renderLock)
        {
            _renderRequested = true;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     The value of the first <c>{name}="..."</c> attribute in the current <see cref="Html" />, or
    ///     <c>null</c> if absent. Action ids live in <c>data-rask-on-{event}</c> attributes.
    /// </summary>
    public string? Attr(string name) => Markup.Attr(Html, name);

    /// <summary>
    ///     The value of every <c>{name}="..."</c> attribute in the current <see cref="Html" />, in document
    ///     order. Empty if none match.
    /// </summary>
    public IReadOnlyList<string> Attrs(string name) => Markup.Attrs(Html, name);

    /// <summary>
    ///     The handler id for the <b>first</b> element wired to <paramref name="domEvent" /> (e.g.
    ///     <c>"click"</c>, <c>"input"</c>, <c>"change"</c>, <c>"submit"</c>), or <c>null</c> if none is
    ///     present. Action ids are reissued from scratch on every render, so an id is valid only for the
    ///     <see cref="Html" /> it was read from — re-query after any re-render rather than reusing a
    ///     captured id. When several elements are wired to the same event, use <see cref="HandlerIds" />;
    ///     the event helpers below always target the first match.
    /// </summary>
    public string? HandlerId(string domEvent) => Attr("data-rask-on-" + domEvent);

    /// <summary>
    ///     The handler ids for <b>every</b> element wired to <paramref name="domEvent" />, in document order
    ///     — index the one under test when a component wires several (a grid's sort headers, a list's row
    ///     buttons): <c>await page.InvokeAsync(page.HandlerIds("click")[2])</c>. Like <see cref="HandlerId" />,
    ///     these are only valid for the render they were read from, so re-read them after every re-render.
    /// </summary>
    public IReadOnlyList<string> HandlerIds(string domEvent) => Attrs("data-rask-on-" + domEvent);

    // ---- structure ----

    /// <summary>
    ///     The single element matching <paramref name="selector" /> in the current render.
    /// </summary>
    /// <remarks>
    ///     Throws when there is no match, and when there is more than one. Both are deliberate: a test that
    ///     silently took the first of several is one that keeps passing after somebody adds a second, and a
    ///     test that silently found nothing is one that asserts about an element that isn't there. The
    ///     message names what was found so the fix is obvious. Use <see cref="FindAll" /> when several
    ///     matches are the point.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="selector" /> is outside the supported subset.</exception>
    /// <exception cref="InvalidOperationException">There is not exactly one match.</exception>
    public HtmlNode Find(string selector)
    {
        var matches = FindAll(selector);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No element matches '{selector}'.{NearMiss(selector)}\n\n{Html}"),
            _ => throw new InvalidOperationException(
                $"{matches.Count} elements match '{selector}', so Find cannot pick one — use FindAll, or "
                + "narrow the selector:\n  "
                + string.Join("\n  ", matches.Take(10).Select(m => m.Path()))),
        };
    }

    /// <summary>
    ///     Every element matching <paramref name="selector" />, in document order. Empty when none match.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="selector" /> is outside the supported subset.</exception>
    public IReadOnlyList<HtmlNode> FindAll(string selector) =>
        HtmlSelector.Select(Root, selector);

    /// <summary>True when at least one element matches <paramref name="selector" />.</summary>
    /// <exception cref="ArgumentException"><paramref name="selector" /> is outside the supported subset.</exception>
    public bool Exists(string selector) => FindAll(selector).Count > 0;

    /// <summary>
    ///     The element carrying <c>data-testid="<paramref name="id" />"</c> — the stable hook to reach for
    ///     when an element has no id of its own and you don't want the test coupled to its classes.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is not exactly one such element.</exception>
    public HtmlNode TestId(string id) => Find($"[data-testid=\"{id}\"]");

    /// <summary>
    ///     The text under the single element matching <paramref name="selector" />, HTML-decoded and with
    ///     runs of whitespace collapsed — what a reader would see, not what the serializer wrote.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is not exactly one match.</exception>
    public string TextOf(string selector) => Normalize(Find(selector).TextContent);

    /// <summary>
    ///     The parsed tree for the current <see cref="Html" />. Reparsed whenever the markup changes, so it
    ///     always reflects the latest render.
    /// </summary>
    public HtmlNode Root
    {
        get
        {
            var html = Html;
            if (!ReferenceEquals(_parsedFrom, html))
            {
                _parsed = HtmlTree.Parse(html);
                _parsedFrom = html;
            }

            return _parsed!;
        }
    }

    // ---- targeting a handler by what it says, not by where it happens to sit ----

    /// <summary>
    ///     The handler id for <paramref name="domEvent" /> on the single element matching
    ///     <paramref name="selector" />.
    /// </summary>
    /// <remarks>
    ///     The reason this exists: <see cref="HandlerId(string)" /> returns the <em>first</em> match in the
    ///     document, and <see cref="HandlerIds(string)" /> is indexed by position — so adding an unrelated
    ///     button above the one under test silently re-points every such assertion at the wrong element,
    ///     and the test keeps passing. Naming the element instead makes the test say what it means.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     There is not exactly one match, or the matched element is not wired to <paramref name="domEvent" />.
    /// </exception>
    public string HandlerIdFor(string selector, string domEvent)
    {
        var node = Find(selector);
        return node.Attribute("data-rask-on-" + domEvent)
               ?? throw new InvalidOperationException(
                   $"'{node.Path()}' matches '{selector}' but has no {domEvent} handler. Wired here: "
                   + Describe(node.Attributes.Keys
                       .Where(k => k.StartsWith("data-rask-on-", StringComparison.Ordinal))
                       .Select(k => k["data-rask-on-".Length..])));
    }

    /// <summary>
    ///     The events of the single element matching <paramref name="selector" />:
    ///     <c>await page.On("#save").ClickAsync()</c>.
    /// </summary>
    /// <remarks>
    ///     A handle rather than <c>ClickAsync(selector)</c> overloads, because the existing
    ///     <see cref="ClickAsync(string?)" /> already takes a <see cref="string" /> — the JSON payload — so a
    ///     selector overload would be chosen by argument count and quietly send "#save" as event args. Two
    ///     meanings for one parameter type is exactly the trap this API is meant to remove.
    /// </remarks>
    /// <exception cref="InvalidOperationException">There is not exactly one match.</exception>
    public ElementActions On(string selector) => new(this, selector);

    /// <summary>The events of one element, resolved by selector. Obtained from <see cref="On" />.</summary>
    /// <param name="page">The rendered component the element belongs to.</param>
    /// <param name="selector">The selector that names it — re-resolved on each call, so it survives re-renders.</param>
    public readonly struct ElementActions(RenderedComponent page, string selector)
    {
        /// <summary>Dispatches this element's <c>click</c> handler, then re-renders.</summary>
        public Task<string> ClickAsync(string? jsonPayload = null) => Raise("click", jsonPayload);

        /// <summary>Raises <c>input</c> with <paramref name="value" /> as the event's value.</summary>
        public Task<string> InputAsync(string value) => Raise("input", JsonValuePayload(value));

        /// <summary>Raises <c>change</c> with <paramref name="value" /> as the event's value.</summary>
        public Task<string> ChangeAsync(string value) => Raise("change", JsonValuePayload(value));

        /// <summary>Dispatches this element's <c>submit</c> handler, then re-renders.</summary>
        public Task<string> SubmitAsync(string? jsonPayload = null) => Raise("submit", jsonPayload);

        /// <summary>Dispatches an arbitrary DOM event by name, for anything without a helper above.</summary>
        public Task<string> RaiseAsync(string domEvent, string? jsonPayload = null) =>
            Raise(domEvent, jsonPayload);

        /// <summary>The element itself, re-resolved from the current render.</summary>
        public HtmlNode Element => page.Find(selector);

        // Resolved per call, not captured: a handler re-renders, and the node from the previous render is
        // then stale. Re-resolving means `var save = page.On("#save")` keeps working across renders.
        private Task<string> Raise(string domEvent, string? jsonPayload) =>
            page.InvokeAsync(page.HandlerIdFor(selector, domEvent), jsonPayload);

        private static string JsonValuePayload(string value) =>
            "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(value) + "}";
    }

    private static string Describe(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? "nothing" : string.Join(", ", list);
    }

    // A selector that matches nothing is nearly always a typo or a stale expectation, and the two read very
    // differently. Saying which part of it does match turns "no element matches" into a pointer.
    private string NearMiss(string selector)
    {
        var head = selector.Split([' ', '>'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (head is null || head == selector)
        {
            return string.Empty;
        }

        try
        {
            var partial = FindAll(head);
            return partial.Count == 0
                ? $" Nor does '{head}'."
                : $" '{head}' matches {partial.Count}, so the rest of the selector is what fails.";
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string Normalize(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Dispatches the handler registered under <paramref name="handlerId" /> with an optional JSON event
    ///     payload (e.g. <c>"{\"value\":\"hi\"}"</c> for an input event), then re-renders and returns the new
    ///     <see cref="Html" />. The id must come from the <b>current</b> render (see <see cref="HandlerId" />).
    /// </summary>
    /// <exception cref="ArgumentException">The payload is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">The id is not a live handler in the current render.</exception>
    public async Task<string> InvokeAsync(string handlerId, string? jsonPayload = null)
    {
        ArgumentNullException.ThrowIfNull(handlerId);

        if (!await DispatchAsync(handlerId, jsonPayload).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"No handler with id '{handlerId}' is registered in the current render. Action ids are "
                + "reissued every render — read the id from the current Html (HandlerId(\"click\") / "
                + "Attr(\"data-rask-on-...\")) immediately before invoking.");
        }

        return Render();
    }

    /// <summary>
    ///     Dispatches <paramref name="handlerId" /> if it is live in the current render, re-rendering and
    ///     returning <c>true</c>; returns <c>false</c> — without re-rendering — if no such handler is
    ///     registered. Use this to assert that a handler is <b>gone</b> (a removed element, a disposed
    ///     subtree); <see cref="InvokeAsync" /> is the ergonomic default when you expect it to be there.
    /// </summary>
    /// <exception cref="ArgumentException">The payload is not valid JSON.</exception>
    public async Task<bool> TryInvokeAsync(string handlerId, string? jsonPayload = null)
    {
        ArgumentNullException.ThrowIfNull(handlerId);

        var dispatched = await DispatchAsync(handlerId, jsonPayload).ConfigureAwait(false);
        if (dispatched)
        {
            Render();
        }

        return dispatched;
    }

    // Parses the payload and hands it to the handler. Returns whether the id was live — the throw-vs-bool
    // choice belongs to the callers above. An invalid payload is a caller bug either way, so it always throws.
    private async Task<bool> DispatchAsync(string handlerId, string? jsonPayload)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonPayload ?? "{}");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"jsonPayload must be a JSON object for the event args, e.g. \"{{\\\"value\\\":\\\"hi\\\"}}\". "
                + $"Got: {jsonPayload}", nameof(jsonPayload), ex);
        }

        using (doc)
        {
            // Enter the Navigator's handler scope for the dispatch, exactly as a live session does.
            // Without it, Navigator.NavigateTo / Download / SetQuery all refuse — "can only be used from
            // event handlers" — which was true of the harness and not of the component: a page that
            // navigates or exports on click could not be unit-tested at all, only through Playwright.
            // No Navigator registered (the common case for a leaf component) means nothing to scope.
            using var scope = (_services.GetService(typeof(Navigator)) as Navigator)?.EnterHandler();

            return await _root.TryInvokeHandlerAsync(handlerId, doc.RootElement, _services)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Dispatches the <b>first</b> <c>click</c> handler in the current render (see <see cref="HandlerId" />).</summary>
    public Task<string> ClickAsync(string? jsonPayload = null) => InvokeEventAsync("click", jsonPayload);

    /// <summary>Dispatches the <b>first</b> <c>input</c> handler, e.g. <c>InputAsync("{\"value\":\"hi\"}")</c>.</summary>
    public Task<string> InputAsync(string? jsonPayload = null) => InvokeEventAsync("input", jsonPayload);

    /// <summary>Dispatches the <b>first</b> <c>change</c> handler.</summary>
    public Task<string> ChangeAsync(string? jsonPayload = null) => InvokeEventAsync("change", jsonPayload);

    /// <summary>Dispatches the <b>first</b> <c>submit</c> handler, e.g. <c>SubmitAsync("{\"form\":{...}}")</c>.</summary>
    public Task<string> SubmitAsync(string? jsonPayload = null) => InvokeEventAsync("submit", jsonPayload);

    // Resolves the event's handler id from the CURRENT render, then dispatches — so the id is never stale.
    private Task<string> InvokeEventAsync(string domEvent, string? jsonPayload)
    {
        var id = HandlerId(domEvent)
                 ?? throw new InvalidOperationException(
                     $"No {domEvent} handler (data-rask-on-{domEvent}) in the current render.");
        return InvokeAsync(id, jsonPayload);
    }
}

/// <summary>
///     A rendered component under test whose <see cref="Instance" /> is the component object itself — for
///     asserting against a component's own state, rather than only the markup it produced.
/// </summary>
/// <typeparam name="T">The component's type.</typeparam>
public sealed class RenderedComponent<T> : RenderedComponent
    where T : Component
{
    internal RenderedComponent(Component root, T instance, IServiceProvider services)
        : base(root, services) => Instance = instance;

    /// <summary>
    ///     The component under test — the very object passed to
    ///     <see cref="RaskTest.Render{T}(T, IServiceProvider)" />, for the lifetime of this handle. The
    ///     forwarding test root renders it directly rather than reconciling it, so this never becomes a
    ///     different instance behind your back.
    /// </summary>
    public T Instance { get; }
}
