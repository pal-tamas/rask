namespace Rask.Example.Shared.Features;

// Cancellation demo promoted out of the former CancellationPage. Mount the probe to start a 2.5s
// Task.Delay inside OnMountAsync; unmount before it settles to cancel via the lifetime token. The probe
// records the outcome into this parent-held log.
public sealed class CancellationDemo : Component
{
    private readonly List<string> _log = new();
    private bool _mounted;
    private int _nextInstance;

    protected override Component? Render() =>
        Div()[
            BsStack(Gap: 2, Class: Margin.Bottom(3))[
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "cancel-mount", Disabled: _mounted, OnClick: Mount)[BsIcon(Name: BsIconName.PlayCircle, Class: "me-1"), "Mount probe"],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "cancel-unmount", Disabled: !_mounted, OnClick: Unmount)[BsIcon(Name: BsIconName.StopCircle, Class: "me-1"), "Unmount probe"]
            ],
            _mounted
                ? CancellationProbe(AppendLog, _nextInstance)
                : P(Class: "text-secondary fst-italic mb-0")["Probe is not mounted."],
            H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
            _log.Count == 0
                ? P(Class: "text-secondary small mb-0")["Mount and unmount the probe to populate this log."]
                : Ol(Class: "list-group list-group-numbered list-group-flush cancel-log", Id: "cancel-log")[
                    _log.Select(line => Li(
                        Key: line,
                        Class: "list-group-item ps-2 small")[Code(Class: "small")[line]])]
        ];

    private void Mount()
    {
        if (_mounted)
        {
            return;
        }

        _nextInstance++;
        _mounted = true;
        StateHasChanged();
    }

    private void Unmount()
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
