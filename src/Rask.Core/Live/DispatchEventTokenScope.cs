namespace Rask.Core.Live;

/// <summary>
///     Ambient <see cref="CancellationToken" /> for the event-handler dispatch currently running on
///     this async flow. Pushed by <c>Component.TryInvokeHandlerAsync</c> around a handler invocation and
///     read back through <c>Component.CancellationToken</c>, so a handler's async work can observe
///     cancellation (a server-side handler timeout, or the socket closing) without the delegate having
///     to take a token parameter. Mirrors <see cref="Rask.Core.Forms.DispatchServicesScope" />; defaults
///     to <see cref="CancellationToken.None" /> outside a dispatch.
/// </summary>
internal static class DispatchEventTokenScope
{
    private static readonly AsyncLocal<CancellationToken> _current = new();

    public static CancellationToken Current => _current.Value;

    public static IDisposable Push(CancellationToken token)
    {
        var prev = _current.Value;
        _current.Value = token;
        return new Popper(prev);
    }

    private sealed class Popper(CancellationToken prev) : IDisposable
    {
        public void Dispose() => _current.Value = prev;
    }
}
