namespace Rask.Example.Shared.Features;

// Cancellation demo promoted out of the former CancellationPage. Mount the probe to start a 2.5s
// Task.Delay inside OnMountAsync; unmount before it settles to cancel via the lifetime token. The probe
// records the outcome into this parent-held log.
public sealed partial class CancellationDemo : Component
{
    private readonly List<string> _log = new();
    private bool _mounted;
    private int _nextInstance;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Tw.BtnPrimary).Id("cancel-mount").Disabled(_mounted).OnClick(MountProbe)[Icon.Name(IconName.PlayCircle).Class("me-1"), "Mount probe"],
                Button.Type("button").Class(Tw.BtnOutlineSecondary)
                    .Id("cancel-unmount")
                    .Disabled(!_mounted)
                    .OnClick(UnmountProbe)[Icon.Name(IconName.StopCircle).Class("me-1"), "Unmount probe"]
            ],
            _mounted
                ? CancellationProbe.Log(AppendLog).InstanceId(_nextInstance)
                : P.Class("text-ui-muted italic mb-0")["Probe is not mounted."],
            H3.Class("text-base font-semibold text-ui-muted uppercase text-sm mt-4")["Log"],
            _log.Count == 0
                ? P.Class("text-ui-muted text-sm mb-0")["Mount and unmount the probe to populate this log."]
                : Ol.Class($"{Tw.ListGroup} list-decimal list-inside divide-y divide-ui-line cancel-log").Id("cancel-log")[
                    _log.Select(line => Li
                        .Key(line)
                        .Class($"{Tw.ListGroupItem} ps-2 text-sm")[Code.Class("text-sm")[line]])]
        ];

    private void MountProbe()
    {
        if (_mounted)
        {
            return;
        }

        _nextInstance++;
        _mounted = true;
        StateHasChanged();
    }

    private void UnmountProbe()
    {
        if (!_mounted)
        {
            return;
        }

        _mounted = false;
        // The probe leaves the tree on the next render, the framework cancels its lifetime token,
        // OnMountAsync's await throws OperationCanceledException, and the catch logs the cancellation
        // via AppendLog — which calls StateHasChanged on us, repainting the log.
        StateHasChanged();
    }

    private void AppendLog(string line)
    {
        _log.Add(line);
        StateHasChanged();
    }
}
