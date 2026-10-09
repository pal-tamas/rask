namespace Rask.Core.Live;

/// <summary>
///     Ambient <see cref="CancellationToken" /> for the event-handler dispatch currently running on
///     this async flow. Pushed by <c>Component.TryInvokeHandlerAsync</c> around a handler invocation and
///     read back through <c>Component.CancellationToken</c>, so a handler's async work can observe
///     cancellation (a server-side handler timeout, or the socket closing) without the delegate having
///     to take a token parameter. Mirrors <see cref="Rask.Core.Forms.DispatchServicesScope" />; defaults
///     to <see cref="CancellationToken.None" /> outside a dispatch.
/// </summary>
/// <remarks>
///     A dispatch's token is two things linked: the lifetime of the component whose code is running, and the
///     host's limit on the dispatch. <see cref="Host" /> keeps the second on its own, so a callback — another
///     component's code, run inside the same dispatch — can link it with ITS lifetime
///     (<see cref="CallbackOwnerScope" />).
/// </remarks>
internal static class DispatchEventTokenScope
{
    private static readonly AsyncLocal<Frame?> _current = new();

    public static CancellationToken Current => _current.Value?.Linked ?? default;

    /// <summary>The host's limit on the dispatch in progress — its handler timeout, or the socket closing.</summary>
    public static CancellationToken Host => _current.Value?.Limit ?? default;

    /// <summary>The component whose lifetime <see cref="Current" /> is linked with, or null outside a dispatch.</summary>
    public static Component? Owner => _current.Value?.Component;

    public static IDisposable Push(CancellationToken token) => Push(null, token, token);

    public static IDisposable Push(Component? owner, CancellationToken host, CancellationToken token)
    {
        var frame = new Frame(_current.Value, owner, host, token);
        _current.Value = frame;
        return frame;
    }

    /// <summary>
    ///     Sets the dispatch's token aside until the scope is disposed — for the render a handler's turn
    ///     runs, whose components answer to their own lifetime. Nothing is written when none is pushed.
    /// </summary>
    public static Suspension Suspend()
    {
        var prev = _current.Value;
        if (prev is null)
        {
            return default;
        }

        _current.Value = null;
        return new Suspension(prev);
    }

    // One object per timed dispatch: what is in force, and what to put back.
    internal sealed class Frame(Frame? previous, Component? owner, CancellationToken host, CancellationToken token)
        : IDisposable
    {
        public CancellationToken Linked => token;

        public CancellationToken Limit => host;

        public Component? Component => owner;

        public void Dispose() => _current.Value = previous;
    }

    internal readonly struct Suspension : IDisposable
    {
        private readonly Frame? _suspended;

        internal Suspension(Frame suspended) => _suspended = suspended;

        public void Dispose()
        {
            if (_suspended is not null)
            {
                _current.Value = _suspended;
            }
        }
    }
}
