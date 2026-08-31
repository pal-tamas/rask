namespace Rask.Example.Shared.Features;

public sealed partial class DisposableAsyncProbe : Component, IAsyncDisposable
{
    private DateTimeOffset _mountedAt;

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    public ValueTask DisposeAsync()
    {
        Log.Invoke($"#{InstanceId} async-disposed (lived {(DateTimeOffset.Now - _mountedAt).TotalMilliseconds:F0} ms)");
        return ValueTask.CompletedTask;
    }

    protected override void OnMount()
    {
        _mountedAt = DateTimeOffset.Now;
        Log.Invoke($"#{InstanceId} async-mounted");
    }

    protected override Component? Render() =>
        Div.Class("flex gap-2 items-center flex-wrap items-center")[
            Span.Class($"{Ui.BadgeInfo} dispose-async-pill")[$"#{InstanceId} alive"],
            Span.Class("text-slate-500 dark:text-slate-400 text-sm")[
                $"Mounted at {_mountedAt:HH:mm:ss.fff}. Unmount me to fire DisposeAsync()."]
        ];
}
