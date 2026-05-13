using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("cancellation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class CancellationPage : Component
{
    private readonly List<string> _log = new();
    private bool _mounted;
    private int _nextInstance;

    protected override Component Render() =>
        Fragment()[
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
            Div(Class: "card shadow-sm border-0 mb-4")[
                Div(Class: "card-body")[
                    Div(Class: "d-flex gap-2 mb-3")[
                        Button(
                            Class: "btn btn-primary btn-sm",
                            Id: "cancel-mount",
                            Disabled: _mounted,
                            OnClick: Mount)[I(Class: "bi bi-play-circle me-1"), "Mount probe"],
                        Button(
                            Class: "btn btn-outline-secondary btn-sm",
                            Id: "cancel-unmount",
                            Disabled: !_mounted,
                            OnClick: Unmount)[I(Class: "bi bi-stop-circle me-1"), "Unmount probe"]
                    ],
                    _mounted
                        ? CancellationProbe(Log: AppendLog, InstanceId: _nextInstance)
                        : P(Class: "text-secondary fst-italic mb-0")["Probe is not mounted."],
                    H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
                    _log.Count == 0
                        ? P(Class: "text-secondary small mb-0")["Mount and unmount the probe to populate this log."]
                        : Ol(Class: "list-group list-group-numbered list-group-flush cancel-log")[_log.Select(line => (Child)Li(
                                Class: "list-group-item ps-2 small")[Code(Class: "small")[line]]).ToArray()]
                ]
            ],
            H2(Class: "h4 mt-4 mb-3")["Source"],
            CodeSample(
                """
                public sealed class CancellationProbe : Component
                {
                    public required Action<string> Log { get; set; }
                    public required int InstanceId { get; set; }

                    protected override async Task OnMountAsync()
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(2500), CancellationToken);
                            Log($"#{InstanceId} completed");
                        }
                        catch (OperationCanceledException)
                        {
                            Log($"#{InstanceId} cancelled");
                        }
                    }
                }
                """,
                Notes:
                "Component.CancellationToken is allocated lazily — components that don't read it never pay the CTS cost. The framework cancels the token before disposing the subtree, so awaits unwind via OperationCanceledException before Dispose runs."),
            Div(Class: "alert alert-info d-flex align-items-start mt-3")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
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
        if (_mounted) return;
        _nextInstance++;
        _mounted = true;
        StateHasChanged();
    }

    private void Unmount()
    {
        if (!_mounted) return;
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
        // The cancel/dispose continuation may land back inside an in-flight dispatch
        // (WASM Task.Run can run inline), so the immediate StateHasChanged would be
        // dropped by the live session's in-handler guard. A yielded follow-up fires
        // after the lock releases and guarantees the log entry reaches the browser.
        _ = DeferredRerenderAsync();
    }

    private async Task DeferredRerenderAsync()
    {
        // Task.Delay (rather than Task.Yield) routes through the runtime timer queue,
        // which always lands on a future event loop tick — guaranteeing we escape the
        // current dispatch's render lock before requesting a follow-up render.
        await Task.Delay(50);
        StateHasChanged();
    }
}
