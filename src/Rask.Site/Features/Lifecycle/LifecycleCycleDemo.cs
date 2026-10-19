namespace Rask.Site.Features;

// The mount/unmount-cycle widget promoted out of the former LifecyclePage so the Lifecycle guide can
// host it as a live demo. Toggling the probe in and out of the tree fires Unmount;
// the log is held here (the parent) so it survives the probe's unmount.
public sealed partial class LifecycleCycleDemo : Component
{
    private readonly List<string> _cycleLog = new();
    private bool _cycleMounted;
    private int _nextCycleId;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Ui.Button.Primary.Icon(Ui.IconName.Play)
                    .Id("lifecycle-cycle-mount")
                    .Disabled(_cycleMounted)
                    .OnClick(MountCycle)["Mount probe"],
                Ui.Button.Icon(Ui.IconName.StopCircle)
                    .Id("lifecycle-cycle-unmount")
                    .Disabled(!_cycleMounted)
                    .OnClick(UnmountCycle)["Unmount probe"]
            ],
            _cycleMounted
                ? LifecycleCycleProbe.InstanceId(_nextCycleId).Log(AppendCycleLog)
                : Ui.Text["Probe not mounted."],
            Ui.Heading.Level(3).Class("mt-4 mb-2")["Log"],
            _cycleLog.Count == 0
                ? Ui.Text["Empty — mount and unmount the probe."]
                : Div.Id("lifecycle-cycle-log")[
                    Ui.Card.Divided.Sm[_cycleLog.Select((l, i) => Ui.CardBody.Key(i)[Ui.Text[l]]).ToArray()]
                ]
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
        _ = DeferredRerender();
    }

    // An unmount-time StateHasChanged lands inside the dispatcher's in-handler guard and gets dropped
    // on WASM. Yielding back to the event loop lets the lock release before we request the follow-up.
    private async Task DeferredRerender()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
