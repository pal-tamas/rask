using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("lifecycle")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class LifecyclePage : Component
{
    private readonly List<string> _cycleLog = new();
    private bool _cycleMounted;
    private int _nextCycleId;

    protected override RenderResult Head => Title()["Lifecycle — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Lifecycle hooks",
            "Every Component can override the lifecycle hooks below. Async hooks install a synchronization context that triggers a re-render after each in-method await, plus one terminal render on completion."),
        H2(Class: "h4 mt-4 mb-3")["Live probe"],
        P(Class: "text-secondary")[
            "The component below records every hook invocation into a list and re-renders so you can watch the order."
        ],
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[LifecycleProbe()]
        ],
        H2(Class: "h4 mt-5 mb-3")["Mount / unmount cycle"],
        P(Class: "text-secondary")[
            "Toggle the probe in and out of the tree to watch ", Code()["OnUnmount"],
            " and ", Code()["OnUnmountAsync"], " fire. The log is held by the parent so it survives the unmount."
        ],
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 mb-3")[
                    Button(
                        Class: "btn btn-primary btn-sm",
                        Id: "lifecycle-cycle-mount",
                        Disabled: _cycleMounted,
                        OnClick: MountCycle)[I(Class: "bi bi-play-circle me-1"), "Mount probe"],
                    Button(
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "lifecycle-cycle-unmount",
                        Disabled: !_cycleMounted,
                        OnClick: UnmountCycle)[I(Class: "bi bi-stop-circle me-1"), "Unmount probe"]
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
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["Source"],
        CodeSample(
            """
            public sealed class LifecycleProbe : Component
            {
                private readonly List<string> _log = new();
                private int _renderCount;

                protected override void OnMount() =>
                    _log.Add("OnMount");

                protected override async Task OnMountAsync()
                {
                    _log.Add("OnMountAsync (start)");
                    await Task.Delay(450);
                    _log.Add("OnMountAsync (after 450ms await)");
                }

                protected override void OnPropsChanged() =>
                    _log.Add($"OnPropsChanged (render #{_renderCount + 1})");

                protected override Task OnPropsChangedAsync()
                {
                    _log.Add("OnPropsChangedAsync");
                    return Task.CompletedTask;
                }

                protected override void OnRendered(bool firstRender) =>
                    _log.Add($"OnRendered(firstRender: {firstRender})");

                // Symmetric with OnMount — fires once when the component leaves the
                // tree (navigation, parent diff, session teardown). The lifetime
                // CancellationToken is still live; it cancels right after.
                protected override void OnUnmount() =>
                    _log.Add("OnUnmount");

                protected override Task OnUnmountAsync()
                {
                    _log.Add("OnUnmountAsync");
                    return Task.CompletedTask;
                }

                public override RenderResult Render()
                {
                    _renderCount++;
                    return /* ... */;
                }
            }
            """,
            Notes:
            "OnMount* fires once on first creation; OnPropsChanged* on first render and whenever a bound prop or route/query param actually changes — a bare event-handler re-render (like the Trigger button above) does NOT refire it; OnRendered* after every render commits; OnUnmount* once on disposal (children before parents). StateHasChanged() inside OnUnmount* is a no-op — the component is already leaving the tree."),
        Div(Class: "alert alert-danger d-flex align-items-start mt-3")[
            I(Class: "bi bi-exclamation-triangle-fill me-3 fs-4"),
            Div()[
                Strong()["Failure model:"],
                " if an async hook faults, the framework logs the exception to ",
                Code()["Console.Error"],
                " and does NOT trigger a re-render — so a component stuck on a loading placeholder is usually a hook that threw."
            ]
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
        _ = DeferredRerenderAsync();
    }

    // Same trick as DisposalPage: an unmount-time StateHasChanged lands inside
    // the dispatcher's in-handler guard and gets dropped on WASM. Yielding back
    // to the event loop lets the lock release before we request the follow-up.
    private async Task DeferredRerenderAsync()
    {
        await Task.Delay(50);
        StateHasChanged();
    }
}
