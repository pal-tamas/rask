namespace Rask.Example.Shared.Features;

public sealed partial class DisposableTimerProbe : Component, IDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    public void Dispose() =>
        Log.Invoke($"#{InstanceId} disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log.Invoke($"#{InstanceId} mounted");
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Span.Class($"{Tw.BadgeWarning} dispose-probe-pill")[$"#{InstanceId} alive"],
            Span.Class("text-slate-500 dark:text-slate-400 text-sm")[$"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire Dispose()."]
        ];
}
