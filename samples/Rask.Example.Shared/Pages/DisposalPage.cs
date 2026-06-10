using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("disposal")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DisposalPage : Component
{
    private readonly List<string> _asyncLog = new();
    private readonly List<string> _hookLog = new();
    private readonly List<string> _syncLog = new();
    private bool _asyncMounted;
    private bool _hookMounted;
    private int _nextAsyncId;
    private int _nextHookId;
    private int _nextSyncId;
    private bool _syncMounted;

    protected override RenderResult Head => Title()["Disposal — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Disposal",
            "Components that implement IDisposable or IAsyncDisposable get their Dispose method called by the framework when they leave the render tree. Use it to release timers, subscriptions, or any handle you took out in OnMount."),
        H2(Class: "h4 mt-4 mb-3")["IDisposable (sync)"],
        P(Class: "text-secondary")[
            "Mount, then unmount. The probe's ", Code()["Dispose()"],
            " runs synchronously as the parent's diff removes it from the tree."
        ],
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 mb-3")[
                    Button(
                        Class: "btn btn-primary btn-sm",
                        Id: "dispose-sync-mount",
                        Disabled: _syncMounted,
                        OnClick: MountSync)[I(Class: "bi bi-play-circle me-1"), "Mount sync probe"],
                    Button(
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "dispose-sync-unmount",
                        Disabled: !_syncMounted,
                        OnClick: UnmountSync)[I(Class: "bi bi-stop-circle me-1"), "Unmount sync probe"]
                ],
                _syncMounted
                    ? DisposableTimerProbe(AppendSyncLog, _nextSyncId)
                    : P(Class: "text-secondary fst-italic mb-0")["Probe not mounted."],
                LogList(_syncLog, "dispose-sync-log")
            ]
        ],
        CodeSample(
            """
            public sealed class DisposableTimerProbe : Component, IDisposable
            {
                public required Action<string> Log { get; set; }
                public required int InstanceId { get; set; }

                protected override void OnMount() => Log($"#{InstanceId} mounted");

                public void Dispose() => Log($"#{InstanceId} disposed");

                protected override RenderResult Render() =>
                    Div()[/* ... */];
            }
            """,
            Notes:
            "Dispose runs once when the component leaves the render tree — either because its parent stopped including it, the route changed, or the live session is being torn down. The framework walks children depth-first, so nested IDisposables dispose bottom-up."),
        H2(Class: "h4 mt-5 mb-3")["IAsyncDisposable"],
        P(Class: "text-secondary")[
            "The async variant: the framework awaits ", Code()["DisposeAsync()"],
            " on its own dispatch path. The log entry shows up after the next render cycle resolves the continuation."
        ],
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 mb-3")[
                    Button(
                        Class: "btn btn-primary btn-sm",
                        Id: "dispose-async-mount",
                        Disabled: _asyncMounted,
                        OnClick: MountAsync)[I(Class: "bi bi-play-circle me-1"), "Mount async probe"],
                    Button(
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "dispose-async-unmount",
                        Disabled: !_asyncMounted,
                        OnClick: UnmountAsync)[I(Class: "bi bi-stop-circle me-1"), "Unmount async probe"]
                ],
                _asyncMounted
                    ? DisposableAsyncProbe(AppendAsyncLog, _nextAsyncId)
                    : P(Class: "text-secondary fst-italic mb-0")["Probe not mounted."],
                LogList(_asyncLog, "dispose-async-log")
            ]
        ],
        Div(Class: "alert alert-warning d-flex align-items-start mt-3")[
            I(Class: "bi bi-exclamation-triangle-fill me-3 fs-4"),
            Div()[
                Strong()["Order:"],
                " disposal walks children depth-first, then cancels the parent's lifetime token, then invokes Dispose / DisposeAsync on the parent. ",
                "If you need to observe the cancellation token from inside Dispose, it is already in the cancelled state by then — see ",
                Code()["/cancellation"], "."
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["OnUnmount vs IDisposable"],
        P(Class: "text-secondary")[
            Code()["OnUnmount"], " / ", Code()["OnUnmountAsync"],
            " is the framework-side cleanup signal. It fires before the lifetime ",
            Code()["CancellationToken"],
            " is cancelled, so cleanup code can still observe the token. Reach for it when the resource is conceptually a ",
            Em()["lifecycle hook"],
            " (unsubscribe from an event, stop a timer you started in ", Code()["OnMount"],
            ") and reserve ", Code()["IDisposable"],
            " for things you would dispose anyway in non-Rask code (file handles, HTTP responses, DB connections)."
        ],
        Div(Class: "card shadow-sm border-0 mb-4")[
            Div(Class: "card-body")[
                Div(Class: "d-flex gap-2 mb-3")[
                    Button(
                        Class: "btn btn-primary btn-sm",
                        Id: "unmount-hook-mount",
                        Disabled: _hookMounted,
                        OnClick: MountHook)[I(Class: "bi bi-play-circle me-1"), "Start ticker"],
                    Button(
                        Class: "btn btn-outline-secondary btn-sm",
                        Id: "unmount-hook-unmount",
                        Disabled: !_hookMounted,
                        OnClick: UnmountHook)[I(Class: "bi bi-stop-circle me-1"), "Stop ticker"]
                ],
                _hookMounted
                    ? UnmountTimerProbe(AppendHookLog, _nextHookId)
                    : P(Class: "text-secondary fst-italic mb-0")["Ticker not running."],
                LogList(_hookLog, "unmount-hook-log")
            ]
        ],
        CodeSample(
            """
            public sealed class UnmountTimerProbe : Component
            {
                public required Action<string> Log { get; set; }
                private Timer? _timer;
                private int _ticks;

                protected override void OnMount()
                {
                    Log("ticker started");
                    _timer = new Timer(_ => {
                        _ticks++;
                        StateHasChanged();
                    }, null, 1000, 1000);
                }

                protected override void OnUnmount()
                {
                    _timer?.Dispose();
                    Log($"ticker stopped after {_ticks} tick(s)");
                }

                protected override RenderResult Render() =>
                    Span()[$"tick {_ticks}"];
            }
            """,
            Notes:
            "No IDisposable on the component. The timer is owned by a render hook, so its cleanup belongs in OnUnmount — symmetric with OnMount. The lifetime CancellationToken is still live here, so you could also pass it to any pending awaits if you wanted to fan out cancellation.")
    ];

    private static Component LogList(IReadOnlyList<string> entries, string id) =>
        Fragment()[
            H3(Class: "h6 text-secondary text-uppercase small mt-4")["Log"],
            entries.Count == 0
                ? P(Class: "text-secondary small mb-0")["Empty — mount and unmount the probe."]
                : Ol(Class: "list-group list-group-numbered list-group-flush",
                    Id: id)[entries.Select((line, i) => Li(Key: i,
                    Class: "list-group-item ps-2 small")[Code(Class: "small")[line]]).ToArray()]];

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

    private void MountAsync()
    {
        if (_asyncMounted)
        {
            return;
        }

        _nextAsyncId++;
        _asyncMounted = true;
        StateHasChanged();
    }

    private void UnmountAsync()
    {
        if (!_asyncMounted)
        {
            return;
        }

        _asyncMounted = false;
        StateHasChanged();
    }

    private void AppendSyncLog(string line)
    {
        _syncLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    private void AppendAsyncLog(string line)
    {
        _asyncLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    private void MountHook()
    {
        if (_hookMounted)
        {
            return;
        }

        _nextHookId++;
        _hookMounted = true;
        StateHasChanged();
    }

    private void UnmountHook()
    {
        if (!_hookMounted)
        {
            return;
        }

        _hookMounted = false;
        StateHasChanged();
    }

    private void AppendHookLog(string line)
    {
        _hookLog.Add(line);
        StateHasChanged();
        _ = DeferredRerenderAsync();
    }

    // The probe's Dispose/DisposeAsync runs inside the parent's render diff pass.
    // Any StateHasChanged we fire from there lands inside the live session's
    // in-handler guard and gets dropped on WASM. Yielding back to the event loop
    // lets the lock release, so a follow-up render actually applies.
    private async Task DeferredRerenderAsync()
    {
        // Task.Delay (rather than Task.Yield) routes through the runtime timer queue,
        // which always lands on a future event loop tick — guaranteeing we escape the
        // current dispatch's render lock before requesting a follow-up render.
        await Task.Delay(50);
        StateHasChanged();
    }
}
