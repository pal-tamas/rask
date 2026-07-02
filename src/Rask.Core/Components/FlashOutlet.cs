using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Messaging;

namespace Rask.Core.Components;

/// <summary>
///     Headless renderer for <see cref="IFlash" /> messages — the display half of Rask's flash
///     (Rails' <c>flash</c>). Mount <b>one</b> in the app layout (so it persists across client-side
///     navigations): it resolves the scoped <see cref="IFlash" />, subscribes to
///     <see cref="IFlash.Changed" />, and <see cref="IFlash.Consume" />s the queue into its own list.
///     The caller owns the markup via <see cref="Template" /> — Core ships no visual (see
///     <c>Rask.Bootstrap</c>'s <c>BsFlash</c> for a ready-made one).
///     <para>
///         Draining moves messages out of the service (consumed-once) and into this outlet, which then
///         owns their on-screen lifetime: the <c>dismiss</c> callback handed to <see cref="Template" />
///         removes a message by <see cref="FlashMessage.Id" /> (wire it to a close button or an
///         auto-hide toast). When no message is showing, the outlet renders nothing.
///     </para>
/// </summary>
public sealed class FlashOutlet : Component
{
    // Own gate: the dismiss callback and Changed handler may run from a timer/thread-pool thread while
    // Render enumerates. Mirrors Flash's own locking discipline.
    private readonly object _gate = new();
    private readonly List<FlashMessage> _messages = [];

    // Cached at mount: LiveRenderContext.Current is null during disposal, so OnUnmount can't re-resolve
    // the service to detach the handler (same reason NavLink caches RouteState).
    private IFlash? _flash;

    /// <summary>
    ///     Renders the currently-showing messages. Receives a <c>dismiss</c> callback that removes a
    ///     message by <see cref="FlashMessage.Id" />. Invoked only when at least one message is showing.
    /// </summary>
    public required Func<IReadOnlyList<FlashMessage>, Action<int>, Component> Template { get; set; }

    // Holds mutable, framework-unobserved state (_messages, drained out-of-band on IFlash.Changed), so
    // the render cache must not pin an earlier snapshot. Same rationale as ValidationMessage.
    protected override bool BypassRenderCache => true;

    protected override void OnMount()
    {
        _flash = LiveRenderContext.Current?.Services?.GetService<IFlash>();
        if (_flash is not null)
        {
            _flash.Changed += OnFlashChanged;
            // Drain anything queued before this outlet mounted — the common case, where a producer set a
            // flash on the previous page and navigated here. OnMount runs before the first Render, so the
            // messages appear on first paint without an explicit StateHasChanged.
            Drain();
        }
    }

    protected override void OnUnmount()
    {
        if (_flash is not null)
        {
            _flash.Changed -= OnFlashChanged;
        }
    }

    protected override Component? Render()
    {
        FlashMessage[] snapshot;
        lock (_gate)
        {
            if (_messages.Count == 0)
            {
                return null;
            }

            snapshot = [.. _messages];
        }

        return Template(snapshot, Dismiss);
    }

    private void OnFlashChanged()
    {
        Drain();
        StateHasChanged();
    }

    private void Drain()
    {
        var incoming = _flash!.Consume();
        if (incoming.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            _messages.AddRange(incoming);
        }
    }

    private void Dismiss(int id)
    {
        lock (_gate)
        {
            _messages.RemoveAll(m => m.Id == id);
        }

        StateHasChanged();
    }
}
