namespace Rask.Core.Messaging;

/// <summary>
///     Default <see cref="IToaster" /> — a thread-safe FIFO queue. Registered scoped per session, so its
///     lifetime spans the session (surviving client-side navigations). Auto-hide timers in a UI layer can
///     add/drain from thread-pool threads, so every access is guarded (the same discipline the toast
///     demos use).
/// </summary>
public sealed class Toaster : IToaster, IToastQueue
{
    private readonly Lock _gate = new();
    private readonly List<ToastMessage> _queue = [];
    private int _nextId;

    // The latest version of each recent toast, drained or not: a step can land after an outlet has drawn the toast —
    // raised off the handler flow, a render on another thread may drain in between — and the outlet reads this on its
    // next render. Bounded: a toast's steps follow it within the same statement.
    private const int Remembered = 32;
    private readonly Dictionary<int, ToastMessage> _recent = [];
    private readonly Queue<int> _recentOrder = new();

    // How many ToastOutlets the APP has mounted in this session. The host's built-in outlet draws nothing while there
    // is one: an app that mounts its own look gets only its own, and no message is split between two outlets.
    private int _appOutlets;

    internal bool HasAppOutlet => Volatile.Read(ref _appOutlets) > 0;

    internal void AppOutletMounted() => Interlocked.Increment(ref _appOutlets);

    internal void AppOutletUnmounted() => Interlocked.Decrement(ref _appOutlets);

    public event EventHandler? Changed;

    public void Add(ToastLevel level, string message, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            var toast = new ToastMessage(_nextId++, level, message, title);
            _queue.Add(toast);
            Remember(toast);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    int IToastQueue.Queue(ToastLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        int id;
        lock (_gate)
        {
            id = _nextId++;
            var toast = new ToastMessage(id, level, message);
            _queue.Add(toast);
            Remember(toast);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    // A step after Toast.X(…) — its title, action, duration. Usually the toast is still queued, since outlets draw on
    // their next render; if an outlet drew it already, it picks the change up from Latest on the render this asks for.
    void IToastQueue.Change(int id, Func<ToastMessage, ToastMessage> change)
    {
        lock (_gate)
        {
            if (!_recent.TryGetValue(id, out var current))
            {
                return;
            }

            var changed = change(current);
            _recent[id] = changed;
            var at = _queue.FindIndex(m => m.Id == id);
            if (at >= 0)
            {
                _queue[at] = changed;
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The latest version of <paramref name="toast" />, with any step taken after it was drawn.</summary>
    internal ToastMessage Latest(ToastMessage toast)
    {
        lock (_gate)
        {
            return _recent.GetValueOrDefault(toast.Id, toast);
        }
    }

    private void Remember(ToastMessage toast)
    {
        _recent[toast.Id] = toast;
        _recentOrder.Enqueue(toast.Id);
        if (_recentOrder.Count > Remembered)
        {
            _recent.Remove(_recentOrder.Dequeue());
        }
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
