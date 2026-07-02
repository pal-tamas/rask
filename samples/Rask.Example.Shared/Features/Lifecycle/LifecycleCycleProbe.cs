namespace Rask.Example.Shared.Features;

// Variant that surfaces every hook — including OnUnmount / OnUnmountAsync — to a parent-held
// log so the unmount entries survive the probe being torn down. The parent owns the list.
public sealed class LifecycleCycleProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount() => Log($"#{InstanceId} OnMount");

    protected override async Task OnMountAsync()
    {
        Log($"#{InstanceId} OnMountAsync (start)");
        await Task.Delay(150);
        Log($"#{InstanceId} OnMountAsync (after 150ms await)");
    }

    protected override void OnUnmount() => Log($"#{InstanceId} OnUnmount");

    protected override Task OnUnmountAsync()
    {
        Log($"#{InstanceId} OnUnmountAsync");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Div(Class: "d-flex align-items-center gap-2")[
            BsBadge(Color: BsColor.Success)[$"#{InstanceId} alive"],
            Span(Class: "text-secondary small")["Unmount me to fire OnUnmount / OnUnmountAsync."]
        ];
}
