namespace Rask.Site.Features;

// Cancellation demo promoted out of the former CancellationPage. Mount the probe to start a 2.5s
// Task.Delay inside Mount; unmount before it settles to cancel via the lifetime token. The probe
// records the outcome into this parent-held log.
public sealed partial class CancellationDemo : Component
{
    private readonly List<string> _log = new();
    private bool _mounted;
    private int _nextInstance;

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Ui.Button.Primary.Icon(Ui.IconName.Play).Id("cancel-mount").Disabled(_mounted).OnClick(MountProbe)["Mount probe"],
                Ui.Button.Icon(Ui.IconName.StopCircle)
                    .Id("cancel-unmount")
                    .Disabled(!_mounted)
                    .OnClick(UnmountProbe)["Unmount probe"]
            ],
            _mounted
                ? CancellationProbe.InstanceId(_nextInstance).Log(AppendLog)
                : Ui.Text["Probe is not mounted."],
            Ui.Heading.Level(3).Class("mt-4 mb-2")["Log"],
            _log.Count == 0
                ? Ui.Text["Mount and unmount the probe to populate this log."]
                // A divided card is Flux's list: one body per line, a hairline between them.
                : Div.Id("cancel-log").Class("cancel-log")[
                    Ui.Card.Divided.Sm[_log.Select(line => Ui.CardBody.Key(line)[Ui.Text[line]])]
                ]
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
        // Mount's await throws OperationCanceledException, and the catch logs the cancellation
        // via AppendLog — which calls StateHasChanged on us, repainting the log.
        StateHasChanged();
    }

    private void AppendLog(string line)
    {
        _log.Add(line);
        StateHasChanged();
    }
}
