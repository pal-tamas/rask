using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Messaging;

namespace Rask.Core.Components;

/// <summary>
///     Headless renderer for <see cref="IToaster" /> messages — the display half of Rask's toasts
///     (a flash-message pattern). Mount <b>one</b> in the app layout (so it persists across client-side
///     navigations): it resolves the scoped <see cref="IToaster" />, subscribes to
///     <see cref="IToaster.Changed" />, and <see cref="IToaster.Consume" />s the queue into its own list.
///     The caller owns the markup via <see cref="Template" /> — Core ships no visual (see
///     <c>Rask.Bootstrap</c>'s <c>BsToaster</c> for a ready-made one).
///     <para>
///         Draining moves messages out of the service (consumed-once) and into this outlet, which then
///         owns their on-screen lifetime: the <c>dismiss</c> callback handed to <see cref="Template" />
///         removes a message by <see cref="ToastMessage.Id" /> (wire it to a close button or an
///         auto-hide toast). When no message is showing, the outlet renders nothing.
///     </para>
///     <para>
///         Set <see cref="AutoDismissAfter" /> to have each shown message remove itself after a delay —
///         a one-shot timer per message that calls the same dismiss path — so any <see cref="Template" />
///         (even one whose element has no timer of its own) auto-dismisses without extra wiring.
///     </para>
/// </summary>
public sealed class ToastOutlet : Component
{
    // Own gate: the dismiss callback, Changed handler and auto-dismiss timers may run from a
    // timer/thread-pool thread while Render enumerates. Mirrors Toaster's own locking discipline.
    private readonly object _gate = new();
    private readonly List<ToastMessage> _messages = [];

    // One-shot auto-dismiss timers, keyed by message id — created on drain when AutoDismissAfter is set,
    // disposed when the message is dismissed (by hand or by the timer itself) or the outlet unmounts.
    private readonly Dictionary<int, Timer> _timers = [];

    // Cached at mount: LiveRenderContext.Current is null during disposal, so OnUnmount can't re-resolve
    // the service to detach the handler (same reason NavLink caches RouteState).
    private IToaster? _toaster;

    /// <summary>
    ///     Renders the currently-showing messages. Receives a <c>dismiss</c> callback that removes a
    ///     message by <see cref="ToastMessage.Id" />. Invoked only when at least one message is showing.
    /// </summary>
    public new required Func<IReadOnlyList<ToastMessage>, Action<int>, Component> Template { get; set; }

    /// <summary>
    ///     When set, each shown message is auto-removed after this delay by a one-shot timer that calls
    ///     the same dismiss path used by the close button. Null (default) keeps every message until it is
    ///     dismissed by hand. A non-positive value is treated as null.
    /// </summary>
    public TimeSpan? AutoDismissAfter { get; set; }

    // Holds mutable, framework-unobserved state (_messages, drained out-of-band on IToaster.Changed), so
    // the render cache must not pin an earlier snapshot. Same rationale as ValidationMessage.
    protected override bool BypassRenderCache => true;

    protected override void OnMount()
    {
        _toaster = LiveRenderContext.Current?.Services?.GetService<IToaster>();
        if (_toaster is not null)
        {
            _toaster.Changed += OnToastChanged;
            // Drain anything queued before this outlet mounted — the common case, where a producer set a
            // toast on the previous page and navigated here. OnMount runs before the first Render, so the
            // messages appear on first paint without an explicit StateHasChanged.
            Drain();
        }
    }

    protected override void OnUnmount()
    {
        if (_toaster is not null)
        {
            _toaster.Changed -= OnToastChanged;
        }

        lock (_gate)
        {
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }

            _timers.Clear();
        }
    }

    protected override Component? Render()
    {
        ToastMessage[] snapshot;
        lock (_gate)
        {
            if (_messages.Count == 0)
            {
                return null;
            }

            snapshot = [.. _messages];
        }

        return Template!(snapshot, Dismiss);
    }

    private void OnToastChanged()
    {
        Drain();
        StateHasChanged();
    }

    private void Drain()
    {
        var incoming = _toaster!.Consume();
        if (incoming.Count == 0)
        {
            return;
        }

        var delay = AutoDismissAfter;
        lock (_gate)
        {
            _messages.AddRange(incoming);
            if (delay is { } d && d > TimeSpan.Zero)
            {
                foreach (var message in incoming)
                {
                    var id = message.Id;
                    _timers[id] = new Timer(_ => Dismiss(id), null, d, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void Dismiss(int id)
    {
        lock (_gate)
        {
            _messages.RemoveAll(m => m.Id == id);
            if (_timers.Remove(id, out var timer))
            {
                timer.Dispose();
            }
        }

        StateHasChanged();
    }
}
