namespace Rask.Example.Shared.Features;

// The mount/unmount-cycle widget promoted out of the former LifecyclePage so the Lifecycle guide can
// host it as a live demo. Toggling the probe in and out of the tree fires OnUnmount / OnUnmountAsync;
// the log is held here (the parent) so it survives the probe's unmount.
public sealed class LifecycleCycleDemo : Component
{
    private readonly List<string> _cycleLog = new();
    private bool _cycleMounted;
    private int _nextCycleId;

    protected override RenderResult Render() =>
        Div()[
            Div(Class: "d-flex gap-2 mb-3")[
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "lifecycle-cycle-mount", Disabled: _cycleMounted, OnClick: MountCycle)[BsIcon(Name: BsIconName.PlayCircle, Class: "me-1"), "Mount probe"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "lifecycle-cycle-unmount", Disabled: !_cycleMounted, OnClick: UnmountCycle)[BsIcon(Name: BsIconName.StopCircle, Class: "me-1"), "Unmount probe"]
            ],
            _cycleMounted
                ? LifecycleCycleProbe(AppendCycleLog, _nextCycleId)
                : P(Class: "text-secondary fst-italic mb-0")["Probe not mounted."],
            H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
            _cycleLog.Count == 0
                ? P(Class: "text-secondary small mb-0")["Empty — mount and unmount the probe."]
                : Ol(
                    Class: "list-group list-group-numbered list-group-flush",
                    Id: "lifecycle-cycle-log")[
                    _cycleLog.Select((l, i) => Li(Key: i,
                        Class: "list-group-item ps-2 small")[Code(Class: "small")[l]]).ToArray()]
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
