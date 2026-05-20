namespace Rask.Example.Shared.Demos;

public sealed class DisposableTimerProbe : Component, IDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    public void Dispose() =>
        Log($"#{InstanceId} disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log($"#{InstanceId} mounted");
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "badge text-bg-warning dispose-probe-pill")[$"#{InstanceId} alive"],
            Span(Class: "text-secondary small")[$"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire Dispose()."]
        ];
}

// Holds a Timer started in OnMount and stopped in OnUnmount. Demonstrates the "use the
// lifecycle hook for things that mirror OnMount" pattern — no IDisposable required.
public sealed class UnmountTimerProbe : Component
{
    private int _ticks;
    private Timer? _timer;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount()
    {
        Log($"#{InstanceId} ticker started");
        _timer = new Timer(_ =>
        {
            Interlocked.Increment(ref _ticks);
            StateHasChanged();
        }, null, 1000, 1000);
    }

    protected override void OnUnmount()
    {
        _timer?.Dispose();
        _timer = null;
        Log($"#{InstanceId} ticker stopped after {_ticks} tick(s)");
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "badge text-bg-warning")[$"#{InstanceId} tick {_ticks}"],
            Span(Class: "text-secondary small")["Stop me to fire OnUnmount and dispose the Timer."]
        ];
}

public sealed class DisposableAsyncProbe : Component, IAsyncDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    public ValueTask DisposeAsync()
    {
        Log($"#{InstanceId} async-disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");
        return ValueTask.CompletedTask;
    }

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log($"#{InstanceId} async-mounted");
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: "badge text-bg-info dispose-async-pill")[$"#{InstanceId} alive"],
            Span(Class: "text-secondary small")[
                $"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire DisposeAsync()."]
        ];
}
