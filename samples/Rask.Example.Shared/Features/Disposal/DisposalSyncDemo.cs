namespace Rask.Example.Shared.Features;

// IDisposable (sync) demo promoted out of the former DisposalPage. Mount, then unmount: the probe's
// Dispose() runs synchronously as the parent's diff removes it from the tree.
public sealed partial class DisposalSyncDemo : Component
{
    private readonly List<string> _syncLog = new();
    private int _nextSyncId;
    private bool _syncMounted;

    protected override Component? Render() =>
        Div[
            Div.Class($"flex gap-2 flex-wrap items-center {"mb-3"}")[
                Button.Type("button").Class(Ui.BtnPrimary)
                    .Id("dispose-sync-mount")
                    .Disabled(_syncMounted)
                    .OnClick(MountSync)[Icon.Name(IconName.PlayCircle).Class("me-1"), "Mount sync probe"],
                Button.Type("button").Class(Ui.BtnOutlineSecondary)
                    .Id("dispose-sync-unmount")
                    .Disabled(!_syncMounted)
                    .OnClick(UnmountSync)[Icon.Name(IconName.StopCircle).Class("me-1"), "Unmount sync probe"]
            ],
            _syncMounted
                ? DisposableTimerProbe.Log(AppendSyncLog).InstanceId(_nextSyncId)
                : P.Class("text-secondary fst-italic mb-0")["Probe not mounted."],
            DisposalDemoLog.Entries(_syncLog).ListId("dispose-sync-log")
        ];

    private void MountSync()
    {
        if (_syncMounted)
        {
            return;
        }

        _nextSyncId++;
        _syncMounted = true;
        StateHasChanged();
    }

    private void UnmountSync()
    {
        if (!_syncMounted)
        {
            return;
        }

        _syncMounted = false;
        StateHasChanged();
    }

    private void AppendSyncLog(string line)
    {
        _syncLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    // The probe's Dispose runs inside the parent's render diff pass; a StateHasChanged fired from there
    // lands inside the live session's in-handler guard and is dropped on WASM. Task.Delay routes through
    // the runtime timer queue (a future event-loop tick), so the render lock is released before the retry.
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
