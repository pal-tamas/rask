namespace Rask.Example.Shared.Features;

// "OnUnmount vs IDisposable" demo promoted out of the former DisposalPage. The probe owns a timer started
// in a render hook, so its cleanup belongs in OnUnmount (symmetric with OnMount) rather than IDisposable.
public sealed partial class DisposalUnmountDemo : Component
{
    private readonly List<string> _hookLog = new();
    private bool _hookMounted;
    private int _nextHookId;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Ui.BtnPrimary)
                    .Id("unmount-hook-mount")
                    .Disabled(_hookMounted)
                    .OnClick(MountHook)[Icon.Name(IconName.PlayCircle).Class("me-1"), "Start ticker"],
                Button.Type("button").Class(Ui.BtnOutlineSecondary)
                    .Id("unmount-hook-unmount")
                    .Disabled(!_hookMounted)
                    .OnClick(UnmountHook)[Icon.Name(IconName.StopCircle).Class("me-1"), "Stop ticker"]
            ],
            _hookMounted
                ? UnmountTimerProbe.Log(AppendHookLog).InstanceId(_nextHookId)
                : P.Class("text-slate-500 dark:text-slate-400 italic mb-0")["Ticker not running."],
            DisposalDemoLog.Entries(_hookLog).ListId("unmount-hook-log")
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
