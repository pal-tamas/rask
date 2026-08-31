namespace Rask.Example.Shared.Features;

// Variant that surfaces every hook — including OnUnmount / OnUnmountAsync — to a parent-held
// log so the unmount entries survive the probe being torn down. The parent owns the list.
public sealed partial class LifecycleCycleProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override void OnMount() => Log.Invoke($"#{InstanceId} OnMount");

    protected override async Task OnMountAsync()
    {
        Log.Invoke($"#{InstanceId} OnMountAsync (start)");
        await Task.Delay(150);
        Log.Invoke($"#{InstanceId} OnMountAsync (after 150ms await)");
    }

    protected override void OnUnmount() => Log.Invoke($"#{InstanceId} OnUnmount");

    protected override Task OnUnmountAsync()
    {
        Log.Invoke($"#{InstanceId} OnUnmountAsync");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Span.Class(Ui.BadgeSuccess)[$"#{InstanceId} alive"],
            Span.Class("text-slate-500 dark:text-slate-400 text-sm")["Unmount me to fire OnUnmount / OnUnmountAsync."]
        ];
}
