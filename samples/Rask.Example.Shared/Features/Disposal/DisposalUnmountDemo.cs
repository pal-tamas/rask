namespace Rask.Example.Shared.Features;

// "OnUnmount vs IDisposable" demo promoted out of the former DisposalPage. The probe owns a timer started
// in a render hook, so its cleanup belongs in OnUnmount (symmetric with OnMount) rather than IDisposable.
public sealed class DisposalUnmountDemo : Component
{
    private readonly List<string> _hookLog = new();
    private bool _hookMounted;
    private int _nextHookId;

    protected override RenderResult Render() =>
        Div()[
            Div(Class: "d-flex gap-2 mb-3")[
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "unmount-hook-mount", Disabled: _hookMounted, OnClick: MountHook)[BsIcon(Name: BsIconName.PlayCircle, Class: "me-1"), "Start ticker"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "unmount-hook-unmount", Disabled: !_hookMounted, OnClick: UnmountHook)[BsIcon(Name: BsIconName.StopCircle, Class: "me-1"), "Stop ticker"]
            ],
            _hookMounted
                ? UnmountTimerProbe(AppendHookLog, _nextHookId)
                : P(Class: "text-secondary fst-italic mb-0")["Ticker not running."],
            DisposalDemoLog.Render(_hookLog, "unmount-hook-log")
        ];

    private void MountHook()
    {
        if (_hookMounted)
        {
            return;
        }

        _nextHookId++;
        _hookMounted = true;
        StateHasChanged();
    }

    private void UnmountHook()
    {
        if (!_hookMounted)
        {
            return;
        }

        _hookMounted = false;
        StateHasChanged();
    }

    private void AppendHookLog(string line)
    {
        _hookLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    // OnUnmount fires inside the parent's render diff pass; yield back to the event loop so the follow-up
    // render that paints the log line escapes the current dispatch's render lock (dropped otherwise on WASM).
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
