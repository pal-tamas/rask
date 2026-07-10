namespace Rask.Core.Messaging;

/// <summary>
///     Default <see cref="IToaster" /> — a thread-safe FIFO queue. Registered scoped per session, so its
///     lifetime spans the session (surviving client-side navigations). Auto-hide timers in a UI layer can
///     add/drain from thread-pool threads, so every access is guarded (the same discipline the toast
///     demos use).
/// </summary>
public sealed class Toaster : IToaster
{
    private readonly object _gate = new();
    private readonly List<ToastMessage> _queue = [];
    private int _nextId;

    public event Action? Changed;

    public void Add(ToastLevel level, string message, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            _queue.Add(new ToastMessage(_nextId++, level, message, title));
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<ToastMessage> Consume()
    {
        lock (_gate)
        {
            if (_queue.Count == 0)
            {
                return [];
            }

            var drained = _queue.ToArray();
            _queue.Clear();
            return drained;
        }
    }
}
