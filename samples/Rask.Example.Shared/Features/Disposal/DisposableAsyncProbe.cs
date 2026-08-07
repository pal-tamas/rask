namespace Rask.Example.Shared.Features;

public sealed partial class DisposableAsyncProbe : Component, IAsyncDisposable
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

    protected override Component? Render() =>
        BsStack(Gap: 2, Align: BsAlign.Center)[
            BsBadge(Color: BsColor.Info, Class: "dispose-async-pill")[$"#{InstanceId} alive"],
            Span(Class: "text-secondary small")[
                $"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire DisposeAsync()."]
        ];
}
