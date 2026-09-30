namespace Rask.Site.Features;

public sealed partial class DisposableAsyncProbe : Component, IAsyncDisposable
{
    private DateTimeOffset _mountedAt;

    public Callback<string> Log { get; set; }
    public required int InstanceId { get; set; }

    public ValueTask DisposeAsync() =>
        Log.Invoke($"#{InstanceId} async-disposed (lived {(TimeProvider.System.GetLocalNow() - _mountedAt).TotalMilliseconds:F0} ms)");

    protected override async Task OnMount()
    {
        _mountedAt = TimeProvider.System.GetLocalNow();
        await Log.Invoke($"#{InstanceId} async-mounted");
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Ui.Badge.Info.Soft.Class("dispose-async-pill")[$"#{InstanceId} alive"],
            Span.Class("text-ui-muted text-sm")[
                $"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire DisposeAsync()."]
        ];
}
