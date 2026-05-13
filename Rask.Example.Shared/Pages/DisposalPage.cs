using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("disposal")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DisposalPage : Component
{
    private readonly List<string> _asyncLog = new();
    private readonly List<string> _syncLog = new();
    private bool _asyncMounted;
    private int _nextAsyncId;
    private int _nextSyncId;
    private bool _syncMounted;

    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Disposal",
                "Components that implement IDisposable or IAsyncDisposable get their Dispose method called by the framework when they leave the render tree. Use it to release timers, subscriptions, or any handle you took out in OnMount."),
            H2(Class: "h4 mt-4 mb-3", Children: ["IDisposable (sync)"]),
            P(Class: "text-secondary", Children:
            [
                "Mount, then unmount. The probe's ", Code(Children: ["Dispose()"]),
                " runs synchronously as the parent's diff removes it from the tree."
            ]),
            Div(Class: "card shadow-sm border-0 mb-4", Children:
            [
                Div(Class: "card-body", Children:
                [
                    Div(Class: "d-flex gap-2 mb-3", Children:
                    [
                        Button(
                            Class: "btn btn-primary btn-sm",
                            Id: "dispose-sync-mount",
                            Disabled: _syncMounted,
                            OnClick: MountSync,
                            Children: [I(Class: "bi bi-play-circle me-1"), "Mount sync probe"]),
                        Button(
                            Class: "btn btn-outline-secondary btn-sm",
                            Id: "dispose-sync-unmount",
                            Disabled: !_syncMounted,
                            OnClick: UnmountSync,
                            Children: [I(Class: "bi bi-stop-circle me-1"), "Unmount sync probe"])
                    ]),
                    _syncMounted
                        ? DisposableTimerProbe(Log: AppendSyncLog, InstanceId: _nextSyncId)
                        : P(Class: "text-secondary fst-italic mb-0",
                            Children: ["Probe not mounted."]),
                    LogList(_syncLog, "dispose-sync-log")
                ])
            ]),
            CodeSample(
                """
                public sealed class DisposableTimerProbe : Component, IDisposable
                {
                    public required Action<string> Log { get; set; }
                    public required int InstanceId { get; set; }

                    protected override void OnMount() => Log($"#{InstanceId} mounted");

                    public void Dispose() => Log($"#{InstanceId} disposed");

                    protected override Component Render() =>
                        Div(Children: [/* ... */]);
                }
                """,
                Notes:
                "Dispose runs once when the component leaves the render tree — either because its parent stopped including it, the route changed, or the live session is being torn down. The framework walks children depth-first, so nested IDisposables dispose bottom-up."),
            H2(Class: "h4 mt-5 mb-3", Children: ["IAsyncDisposable"]),
            P(Class: "text-secondary", Children:
            [
                "The async variant: the framework awaits ", Code(Children: ["DisposeAsync()"]),
                " on its own dispatch path. The log entry shows up after the next render cycle resolves the continuation."
            ]),
            Div(Class: "card shadow-sm border-0 mb-4", Children:
            [
                Div(Class: "card-body", Children:
                [
                    Div(Class: "d-flex gap-2 mb-3", Children:
                    [
                        Button(
                            Class: "btn btn-primary btn-sm",
                            Id: "dispose-async-mount",
                            Disabled: _asyncMounted,
                            OnClick: MountAsync,
                            Children: [I(Class: "bi bi-play-circle me-1"), "Mount async probe"]),
                        Button(
                            Class: "btn btn-outline-secondary btn-sm",
                            Id: "dispose-async-unmount",
                            Disabled: !_asyncMounted,
                            OnClick: UnmountAsync,
                            Children: [I(Class: "bi bi-stop-circle me-1"), "Unmount async probe"])
                    ]),
                    _asyncMounted
                        ? DisposableAsyncProbe(Log: AppendAsyncLog, InstanceId: _nextAsyncId)
                        : P(Class: "text-secondary fst-italic mb-0",
                            Children: ["Probe not mounted."]),
                    LogList(_asyncLog, "dispose-async-log")
                ])
            ]),
            Div(Class: "alert alert-warning d-flex align-items-start mt-3", Children:
            [
                I(Class: "bi bi-exclamation-triangle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Order:"]),
                    " disposal walks children depth-first, then cancels the parent's lifetime token, then invokes Dispose / DisposeAsync on the parent. ",
                    "If you need to observe the cancellation token from inside Dispose, it is already in the cancelled state by then — see ",
                    Code(Children: ["/cancellation"]), "."
                ])
            ])
        );

    private static Component LogList(IReadOnlyList<string> entries, string id) =>
        Fragment(
            H3(Class: "h6 text-secondary text-uppercase small mt-4", Children: ["Log"]),
            entries.Count == 0
                ? P(Class: "text-secondary small mb-0", Children: ["Empty — mount and unmount the probe."])
                : Ol(Class: "list-group list-group-numbered list-group-flush",
                    Id: id,
                    Children: entries.Select(line => (Child)Li(
                        Class: "list-group-item ps-2 small",
                        Children: [Code(Class: "small", Children: [line])])).ToArray()));

    private void MountSync()
    {
        if (_syncMounted) return;
        _nextSyncId++;
        _syncMounted = true;
        StateHasChanged();
    }

    private void UnmountSync()
    {
        if (!_syncMounted) return;
        _syncMounted = false;
        StateHasChanged();
    }

    private void MountAsync()
    {
        if (_asyncMounted) return;
        _nextAsyncId++;
        _asyncMounted = true;
        StateHasChanged();
    }

    private void UnmountAsync()
    {
        if (!_asyncMounted) return;
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
