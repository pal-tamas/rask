namespace Rask.Site.Features;

// Variant that surfaces every hook — including OnMount / OnUnmount — to a parent-held
// log so the unmount entries survive the probe being torn down. The parent owns the list.
public sealed partial class LifecycleCycleProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task OnMount()
    {
        // Above the first await, so it still runs before the first render — which is what the
        // synchronous twin used to be for.
        Log.Invoke($"#{InstanceId} OnMount (before its await)");
        await Task.Delay(150);
        Log.Invoke($"#{InstanceId} OnMount (after a 150ms await)");
    }

    protected override Task OnUnmount()
    {
        Log.Invoke($"#{InstanceId} OnUnmount");
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Ui.Badge.Tone(Ui.Tone.Success).Variant(Ui.Variant.Soft)[$"#{InstanceId} alive"],
            Span.Class("text-ui-muted text-sm")["Unmount me to fire OnUnmount."]
        ];
}
