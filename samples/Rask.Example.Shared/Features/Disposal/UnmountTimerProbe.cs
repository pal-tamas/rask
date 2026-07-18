namespace Rask.Example.Shared.Features;

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

    protected override Component? Render() =>
        BsStack(Gap: 2, Align: BsAlign.Center)[
            BsBadge(Color: BsColor.Warning)[$"#{InstanceId} tick {_ticks}"],
            Span(Class: "text-secondary small")["Stop me to fire OnUnmount and dispose the Timer."]
        ];
}
