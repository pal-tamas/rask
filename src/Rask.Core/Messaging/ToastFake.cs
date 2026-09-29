namespace Rask.Core.Messaging;

/// <summary>
///     A test's stand-in for the session's toasts: <c>using var toasts = Toast.Fake();</c> records every
///     <c>Toast.X(…)</c> in this test's flow, for <c>toasts.Shown("Saved").Once()</c>.
/// </summary>
public sealed class ToastFake : IToastQueue, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<ToastMessage> _shown = [];
    private readonly IToastQueue? _previous;

    internal ToastFake()
    {
        _previous = Toast.Faked.Value;
        Toast.Faked.Value = this;
    }

    /// <summary>The toasts that said <paramref name="message" />, for <c>.Once()</c>, <c>.Never()</c> or <c>.As(…)</c>.</summary>
    /// <param name="message">What the toast said.</param>
    public ShownToasts Shown(string message)
    {
        lock (_gate)
        {
            return new ShownToasts(message, [.. _shown], [.. _shown.Where(t => string.Equals(t.Message, message, StringComparison.Ordinal))]);
        }
    }

    /// <summary>Stops recording; the next toast goes to the session again.</summary>
    public void Dispose() => Toast.Faked.Value = _previous;

    int IToastQueue.Queue(ToastLevel level, string message)
    {
        lock (_gate)
        {
            _shown.Add(new ToastMessage(_shown.Count, level, message));
            return _shown.Count - 1;
        }
    }

    void IToastQueue.Change(int id, Func<ToastMessage, ToastMessage> change)
    {
        lock (_gate)
        {
            _shown[id] = change(_shown[id]);
        }
    }
}
