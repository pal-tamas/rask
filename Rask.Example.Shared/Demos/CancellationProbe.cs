using System.Diagnostics;

namespace Rask.Example.Shared.Demos;

public sealed class CancellationProbe : Component
{
    private readonly Stopwatch _watch = new();
    private string _status = "pending";

    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task OnMountAsync()
    {
        // Capture the lifetime token ONCE up-front. Reading Component.CancellationToken
        // after the framework has disposed the underlying CTS would lazily allocate a
        // fresh (uncancelled) CTS — masking the very signal we're trying to observe.
        var token = CancellationToken;
        _watch.Start();
        _status = "running";
        // Make the "running" pill visible BEFORE the long await — the framework's
        // post-await StateHasChanged only fires after the continuation resumes, so
        // without this the user would jump straight from "pending" to "completed".
        StateHasChanged();
        try
        {
            // Cooperative cancellation in 100ms slices. We poll the captured token
            // rather than passing it into Task.Delay because, on single-threaded WASM,
            // a Task.Delay cancellation raised from inside the dispatch lock doesn't
            // always resume the await — polling at every slice boundary guarantees
            // we notice the cancellation within ~100ms of it being requested.
            while (_watch.ElapsedMilliseconds < 2500)
            {
                await Task.Delay(100);
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }
            }

            _watch.Stop();
            _status = "completed";
            Log($"#{InstanceId} completed ({_watch.ElapsedMilliseconds} ms)");
        }
        catch (OperationCanceledException)
        {
            _watch.Stop();
            // Probe is being unmounted, so re-rendering itself is moot — but the
            // page owns the log list and re-renders, so the entry is still visible.
            Log($"#{InstanceId} cancelled ({_watch.ElapsedMilliseconds} ms)");
        }
    }

    protected override Component Render()
    {
        var pillClass = _status switch
        {
            "running" => "badge text-bg-warning",
            "completed" => "badge text-bg-success",
            _ => "badge text-bg-secondary"
        };

        return Div(Class: "d-flex align-items-center gap-2")[
            Span(Class: $"{pillClass} cancel-probe-pill")[$"#{InstanceId} {_status}"],
            Span(Class: "text-secondary small")[
                _status == "running"
                    ? "Awaiting Task.Delay(2500ms, CancellationToken). Click Unmount to abort."
                    : "Awaited task settled — probe is still alive."
            ]
        ];
    }
}
