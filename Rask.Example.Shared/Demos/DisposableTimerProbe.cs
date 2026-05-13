namespace Rask.Example.Shared.Demos;

public sealed class DisposableTimerProbe : Component, IDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log($"#{InstanceId} mounted");
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2", Children:
        [
            Span(Class: "badge text-bg-warning dispose-probe-pill",
                Children: [$"#{InstanceId} alive"]),
            Span(Class: "text-secondary small",
                Children: [$"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire Dispose()."])
        ]);

    public void Dispose() =>
        Log($"#{InstanceId} disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");
}

public sealed class DisposableAsyncProbe : Component, IAsyncDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log($"#{InstanceId} async-mounted");
    }

    protected override Component Render() =>
        Div(Class: "d-flex align-items-center gap-2", Children:
        [
            Span(Class: "badge text-bg-info dispose-async-pill",
                Children: [$"#{InstanceId} alive"]),
            Span(Class: "text-secondary small",
                Children: [$"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire DisposeAsync()."])
        ]);

    public ValueTask DisposeAsync()
    {
        Log($"#{InstanceId} async-disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");
        return ValueTask.CompletedTask;
    }
}
