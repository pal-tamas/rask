using System.Diagnostics;
using System.Text.Json;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Testing;

/// <summary>
///     A rendered component under test. <see cref="Html" /> is the current markup; invoke a handler
///     (<see cref="InvokeAsync" />, <see cref="ClickAsync" />) to simulate an event, which dispatches it
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
    ///     <c>null</c> if absent. Handler ids live in <c>data-rask-on-{event}</c> attributes.
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
    ///     present. Handler ids are reissued from scratch on every render, so an id is valid only for the
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
                $"No handler with id '{handlerId}' is registered in the current render. Handler ids are "
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
