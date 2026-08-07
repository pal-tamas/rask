namespace Rask.Example.Shared.Features;

// IAsyncDisposable demo promoted out of the former DisposalPage. The framework awaits DisposeAsync() on
// its own dispatch path; the log entry appears after the next render cycle resolves the continuation.
public sealed partial class DisposalAsyncDemo : Component
{
    private readonly List<string> _asyncLog = new();
    private bool _asyncMounted;
    private int _nextAsyncId;

    protected override Component? Render() =>
        Div()[
            BsStack(Gap: 2, Class: Margin.Bottom(3))[
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "dispose-async-mount", Disabled: _asyncMounted, OnClick: MountAsync)[BsIcon(Name: BsIconName.PlayCircle, Class: "me-1"), "Mount async probe"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "dispose-async-unmount", Disabled: !_asyncMounted, OnClick: UnmountAsync)[BsIcon(Name: BsIconName.StopCircle, Class: "me-1"), "Unmount async probe"]
            ],
            _asyncMounted
                ? DisposableAsyncProbe(AppendAsyncLog, _nextAsyncId)
                : P(Class: "text-secondary fst-italic mb-0")["Probe not mounted."],
            DisposalDemoLog.Render(_asyncLog, "dispose-async-log")
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
