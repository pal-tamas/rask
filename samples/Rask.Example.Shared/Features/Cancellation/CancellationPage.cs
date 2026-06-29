using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("cancellation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CancellationPage : Component
{
    private readonly List<string> _log = new();
    private bool _mounted;
    private int _nextInstance;

    protected override RenderResult Head => Title()["Cancellation — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Cancellation",
            "Every Component exposes a protected CancellationToken that fires exactly once when the component is unmounted. Pass it into HttpClient calls, Task.Delay, or any other cancellable async work started inside a lifecycle hook."),
        H2(Class: "h4 mt-4 mb-3")["Live probe"],
        P(Class: "text-secondary")[
            "Mount the probe to start a 2.5-second ",
            Code()["Task.Delay"],
            " inside ", Code()["OnMountAsync"],
            ". Click Unmount before it settles to cancel — the probe records what happened into the log below."
        ],
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))[
            BsCardBody()[
                Div(Class: "d-flex gap-2 mb-3")[
                    BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "cancel-mount", Disabled: _mounted, OnClick: Mount)[BsIcon(Name: BsIconName.PlayCircle, Class: "me-1"), "Mount probe"],
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "cancel-unmount", Disabled: !_mounted, OnClick: Unmount)[BsIcon(Name: BsIconName.StopCircle, Class: "me-1"), "Unmount probe"]
                ],
                _mounted
                    ? CancellationProbe(AppendLog, _nextInstance)
                    : P(Class: "text-secondary fst-italic mb-0")["Probe is not mounted."],
                H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
                _log.Count == 0
                    ? P(Class: "text-secondary small mb-0")["Mount and unmount the probe to populate this log."]
                    : Ol(Class: "list-group list-group-numbered list-group-flush cancel-log")[_log.Select(line =>
                        Li(
                            Key: line,
                            Class: "list-group-item ps-2 small")[Code(Class: "small")[line]])]
            ]
        ],
        H2(Class: "h4 mt-4 mb-3")["Source"],
        CodeSample(
            ["CancellationProbe.cs"],
            Notes:
            "Component.CancellationToken is allocated lazily — components that don't read it never pay the CTS cost. The framework cancels the token before disposing the subtree, so awaits unwind via OperationCanceledException before Dispose runs."),
        BsAlert(Color: BsColor.Info, Class: "d-flex align-items-start mt-3")[
            BsIcon(Name: BsIconName.InfoCircleFill, Class: "me-3 fs-4"),
            Div()[
                Strong()["Cooperation required:"],
                " the framework does not abort blocking calls — it only signals the token. Pass it into ",
                Code()["HttpClient"], ", ", Code()["Task.Delay"],
                ", and any cancellable API you call from a lifecycle hook so they unwind cleanly when the user navigates away."
            ]
        ]
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
        // The probe leaves the tree on the next render, the framework cancels its
        // lifetime token, OnMountAsync's await throws OperationCanceledException,
        // and the catch logs the cancellation via AppendLog — which calls
        // StateHasChanged on us, repainting the log.
        StateHasChanged();
    }

    private void AppendLog(string line)
    {
        _log.Add(line);
        StateHasChanged();
    }
}
