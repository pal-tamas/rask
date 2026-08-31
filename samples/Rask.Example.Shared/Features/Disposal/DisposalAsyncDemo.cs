namespace Rask.Example.Shared.Features;

// IAsyncDisposable demo promoted out of the former DisposalPage. The framework awaits DisposeAsync() on
// its own dispatch path; the log entry appears after the next render cycle resolves the continuation.
public sealed partial class DisposalAsyncDemo : Component
{
    private readonly List<string> _asyncLog = new();
    private bool _asyncMounted;
    private int _nextAsyncId;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Ui.BtnPrimary)
                    .Id("dispose-async-mount")
                    .Disabled(_asyncMounted)
                    .OnClick(MountAsync)[Icon.Name(IconName.PlayCircle).Class("me-1"), "Mount async probe"],
                Button.Type("button").Class(Ui.BtnOutlineSecondary)
                    .Id("dispose-async-unmount")
                    .Disabled(!_asyncMounted)
                    .OnClick(UnmountAsync)[Icon.Name(IconName.StopCircle).Class("me-1"), "Unmount async probe"]
            ],
            _asyncMounted
                ? DisposableAsyncProbe.Log(AppendAsyncLog).InstanceId(_nextAsyncId)
                : P.Class("text-slate-500 dark:text-slate-400 italic mb-0")["Probe not mounted."],
            DisposalDemoLog.Entries(_asyncLog).ListId("dispose-async-log")
        ];

    private void MountAsync()
    {
        if (_asyncMounted)
        {
            return;
        }

        _nextAsyncId++;
        _asyncMounted = true;
        StateHasChanged();
    }

    private void UnmountAsync()
    {
        if (!_asyncMounted)
        {
            return;
        }

        _asyncMounted = false;
        StateHasChanged();
    }

    private void AppendAsyncLog(string line)
    {
        _asyncLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    // DisposeAsync resolves on a later dispatch; yield back to the event loop so the follow-up render
    // that paints the log line escapes the current dispatch's render lock (dropped otherwise on WASM).
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
