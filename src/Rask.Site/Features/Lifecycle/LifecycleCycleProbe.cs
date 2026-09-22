namespace Rask.Site.Features;

// Variant that surfaces Mount and Unmount to a parent-held log, so the unmount entry survives the probe
// being torn down. The parent owns the list.
public sealed partial class LifecycleCycleProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task Mount()
    {
        Log.Invoke($"#{InstanceId} Mount (before its await)");
        await Task.Delay(150);
        Log.Invoke($"#{InstanceId} Mount (after a 150ms await)");
    }

    protected override async Task Unmount() => Log.Invoke($"#{InstanceId} Unmount");

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            UiBadge.Tone(UiTone.Success).Variant(UiVariant.Soft)[$"#{InstanceId} alive"],
            Span.Class("text-ui-muted text-sm")["Unmount me to fire Unmount."]
        ];
}
