using System.Diagnostics;

namespace Rask.Example.Shared.Features;

public sealed class CancellationProbe : Component
{
    private readonly Stopwatch _watch = new();
    private int _logged;
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

        // Synchronous cancellation observer: fires the instant the framework calls
        // Cancel() on our lifetime CTS (inside DisposeComponentTree, before the loop's
        // next Task.Delay continuation would otherwise resume). On WASM CI the polling
        // continuation can be delayed enough by event-loop contention to push the test
        // past its 10s timeout — registering here makes the "cancelled" log entry
        // appear in lock-step with the framework's dispose pass, independent of any
        // scheduler latency. Interlocked guards against the loop's own cancellation
        // observation logging a duplicate entry.
        using var registration = token.Register(static state =>
        {
            var probe = (CancellationProbe)state!;
            if (Interlocked.Exchange(ref probe._logged, 1) != 0)
            {
                return;
            }

            if (probe._watch.IsRunning)
            {
                probe._watch.Stop();
            }

            probe.Log($"#{probe.InstanceId} cancelled ({probe._watch.ElapsedMilliseconds} ms)");
        }, this);

        // Cooperative cancellation in 100ms slices. We poll the captured token
        // rather than passing it into Task.Delay because, on single-threaded WASM,
        // a Task.Delay cancellation raised from inside the dispatch lock doesn't
        // always resume the await — polling at every slice boundary guarantees
        // we notice the cancellation within ~100ms of it being requested even
        // when the Register callback above somehow missed.
        while (_watch.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(100);
            if (token.IsCancellationRequested)
            {
                return;
            }
        }

        if (Interlocked.Exchange(ref _logged, 1) != 0)
        {
            return;
        }

        _watch.Stop();
        _status = "completed";
        Log($"#{InstanceId} completed ({_watch.ElapsedMilliseconds} ms)");
    }

    protected override RenderResult Render()
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
