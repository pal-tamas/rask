namespace Rask.Site.Features;

// Holds a Timer started in Mount and stopped in Unmount. Demonstrates the "use the
// lifecycle hook for things that mirror Mount" pattern — no IDisposable required.
#pragma warning disable CA1001 // disposed in OnUnmount, the lifecycle hook this demo teaches instead of IDisposable
public sealed partial class UnmountTimerProbe : Component
#pragma warning restore CA1001
{
    private int _ticks;
    private Timer? _timer;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task OnMount()
    {
        Log.Invoke($"#{InstanceId} ticker started");
        _timer = new Timer(_ =>
        {
            Interlocked.Increment(ref _ticks);
            StateHasChanged();
        }, null, 1000, 1000);
    }

    protected override async Task OnUnmount()
    {
        if (_timer is not null)
        {
            await _timer.DisposeAsync();
            _timer = null;
        }
        Log.Invoke($"#{InstanceId} ticker stopped after {_ticks} tick(s)");
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Ui.Badge.Tone(Ui.Tone.Warning).Variant(Ui.Variant.Soft)[$"#{InstanceId} tick {_ticks}"],
            Span.Class("text-ui-muted text-sm")["Stop me to fire OnUnmount and dispose the Timer."]
        ];
}
