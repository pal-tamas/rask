using System.Text.Json;
using Rask.Core;

namespace Rask.Testing;

/// <summary>
///     A rendered component under test. <see cref="Html" /> is the current markup; invoke a handler
///     (<see cref="InvokeAsync" />, <see cref="ClickAsync" />) to simulate an event, which dispatches it
///     and re-renders, or call <see cref="Render" /> to re-render after mutating external state.
/// </summary>
public sealed class RenderedComponent
{
    private readonly Component _root;
    private readonly IServiceProvider _services;

    internal RenderedComponent(Component root, IServiceProvider services)
    {
        _root = root;
        _services = services;
        Html = _root.RenderAsLiveRoot(_services);
    }

    /// <summary>The current rendered HTML, reflecting the component's state as of the last render.</summary>
    public string Html { get; private set; }

    /// <summary>Re-renders the component (e.g. after mutating state it reads) and refreshes <see cref="Html" />.</summary>
    public string Render()
    {
        Html = _root.RenderAsLiveRoot(_services);
        return Html;
    }

    /// <summary>
    ///     The value of the first <c>{name}="..."</c> attribute in the current <see cref="Html" />, or
    ///     <c>null</c> if absent. Handler ids live in <c>data-rask-on-{event}</c> attributes.
    /// </summary>
    public string? Attr(string name) => MarkupQuery.Attr(Html, name);

    /// <summary>
    ///     The handler id for the <b>first</b> element wired to <paramref name="domEvent" /> (e.g.
    ///     <c>"click"</c>, <c>"input"</c>, <c>"change"</c>, <c>"submit"</c>), or <c>null</c> if none is
    ///     present. Handler ids are reissued from scratch on every render, so an id is valid only for the
    ///     <see cref="Html" /> it was read from — re-query after any re-render rather than reusing a
    ///     captured id. For a component with several elements wired to the same event, prefer giving the
    ///     target an <c>Id</c> and reading its handler off <see cref="Html" />; the event helpers below
    ///     always target the first match.
    /// </summary>
    public string? HandlerId(string domEvent) => Attr("data-rask-on-" + domEvent);

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
            var dispatched = await _root.TryInvokeHandlerAsync(handlerId, doc.RootElement, _services)
                .ConfigureAwait(false);
            if (!dispatched)
            {
                throw new InvalidOperationException(
                    $"No handler with id '{handlerId}' is registered in the current render. Handler ids are "
                    + "reissued every render — read the id from the current Html (HandlerId(\"click\") / "
                    + "Attr(\"data-rask-on-...\")) immediately before invoking.");
            }
        }

        return Render();
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
