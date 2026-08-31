namespace Rask.Example.Shared.Features;

// The mount/unmount-cycle widget promoted out of the former LifecyclePage so the Lifecycle guide can
// host it as a live demo. Toggling the probe in and out of the tree fires OnUnmount / OnUnmountAsync;
// the log is held here (the parent) so it survives the probe's unmount.
public sealed partial class LifecycleCycleDemo : Component
{
    private readonly List<string> _cycleLog = new();
    private bool _cycleMounted;
    private int _nextCycleId;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Ui.BtnPrimary)
                    .Id("lifecycle-cycle-mount")
                    .Disabled(_cycleMounted)
                    .OnClick(MountCycle)[Icon.Name(IconName.PlayCircle).Class("me-1"), "Mount probe"],
                Button.Type("button").Class(Ui.BtnOutlineSecondary)
                    .Id("lifecycle-cycle-unmount")
                    .Disabled(!_cycleMounted)
                    .OnClick(UnmountCycle)[Icon.Name(IconName.StopCircle).Class("me-1"), "Unmount probe"]
            ],
            _cycleMounted
                ? LifecycleCycleProbe.Log(AppendCycleLog).InstanceId(_nextCycleId)
                : P.Class("text-slate-500 dark:text-slate-400 italic mb-0")["Probe not mounted."],
            H3.Class("text-base font-semibold text-slate-500 dark:text-slate-400 uppercase text-sm mt-4")["Log"],
            _cycleLog.Count == 0
                ? P.Class("text-slate-500 dark:text-slate-400 text-sm mb-0")["Empty — mount and unmount the probe."]
                : Ol
                    .Class($"{Ui.ListGroup} list-decimal list-inside divide-y divide-slate-200 dark:divide-slate-700")
                    .Id("lifecycle-cycle-log")[
                    _cycleLog.Select((l, i) => Li
                        .Key(i)
                        .Class($"{Ui.ListGroupItem} ps-2 text-sm")[Code.Class("text-sm")[l]]).ToArray()]
        ];

    private void MountCycle()
    {
        if (_cycleMounted)
        {
            return;
        }

        _nextCycleId++;
        _cycleMounted = true;
        StateHasChanged();
    }

    private void UnmountCycle()
    {
        if (!_cycleMounted)
        {
            return;
        }

        _cycleMounted = false;
        StateHasChanged();
    }

    private void AppendCycleLog(string line)
    {
        _cycleLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    // An unmount-time StateHasChanged lands inside the dispatcher's in-handler guard and gets dropped
    // on WASM. Yielding back to the event loop lets the lock release before we request the follow-up.
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
