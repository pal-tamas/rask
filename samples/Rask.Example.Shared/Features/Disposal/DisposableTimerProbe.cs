namespace Rask.Example.Shared.Features;

public sealed partial class DisposableTimerProbe : Component, IDisposable
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

    protected override Component? Render() =>
        BsStack.Gap(2).Align(BsAlign.Center)[
            BsBadge.Color(BsColor.Warning).Class("dispose-probe-pill")[$"#{InstanceId} alive"],
            Span.Class("text-secondary small")[$"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire Dispose()."]
        ];
}
