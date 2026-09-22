using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

// Regression tests for the FIFO handler-dispatch contract.
//
// Until the fix in RaskEndpointExtensions, the WS receive loop wrapped each
// inbound handler dispatch in `Task.Run(() => DispatchHandlerAsync(...))` and
// trusted `session.Lock`'s SemaphoreSlim to preserve arrival order. That
// trust was misplaced: SemaphoreSlim is FIFO based on the *order callers
// invoke WaitAsync*, not the order their wrapping Task.Run was created. Under
// ThreadPool contention two handlers spawned in input→submit order could
// acquire the lock in submit→input order — letting submit read a stale
// EditContext that the preceding input had not yet applied.
//
// The fix chains handler dispatches via LiveSession.LastHandlerTask, so the
// next dispatch awaits the previous one's completion before running. The
// receive loop still spawns each chain link as a fire-and-forget task (so
// async handlers awaiting jsResult / dotNetInvoke don't deadlock the loop),
// but the *start order* of dispatches now matches WS arrival order
// deterministically.
//
// Both transports: over HTTP the order is the POSTs', which the browser keeps by holding one in flight and
// batching what arrives behind it — a server that dispatched a batch's frames out of order, or ran two POSTs'
// frames concurrently, would reorder input and submit exactly as the old socket loop did.
public class HandlerOrderingTests
{
    // These tests assert against the `html` field in the payload — the legacy
    // ship-full-HTML wire shape. The framework default is LiveDiffMode.Auto, which ships
    // diff payloads (`kind: "diff"` with ops). Each test host is created with
    // diffMode: DisabledFull (per-host on the LiveSessionStore, not a global) so the
    // parse-html assertions remain meaningful and the class still runs in parallel.

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Ten_handlers_sent_rapidly_dispatch_in_arrival_order(LiveTransportKind transport)
    {
        // Saturate the ThreadPool first so the dispatcher's continuations have
        // to compete for workers. Without contention, the ThreadPool happens
        // to schedule continuations in FIFO order on most machines and the
        // bug stays latent — which is exactly why the failure only surfaces
        // on loaded CI runners. Make the test fail deterministically when
        // chaining is broken by injecting that contention here.
        using var stress = new ThreadPoolStress(8);

        using var host = RaskTestHost.Create<OrderedDispatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerIds = ExtractAllHandlerIds(initialHtml);
        Assert.Equal(10, handlerIds.Count);

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        // Send all ten handler messages back-to-back. The OrderedDispatchApp
        // yields inside each handler (Task.Yield before mutating Sequence)
        // so the continuation runs on a fresh ThreadPool tick — exactly the
        // window where non-FIFO scheduling would let a later-spawned handler
        // run ahead.
        for (var i = 0; i < handlerIds.Count; i++)
        {
            await ws.SendJsonAsync(new { id = handlerIds[i] });
        }

        // Drain renders until Sequence contains all ten digits, or fail.
        string? finalSequence = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
            if (text is null)
            {
                continue;
            }

            var match = Regex.Match(text, "Sequence=([0-9]*)");
            if (match.Success && match.Groups[1].Value.Length == 10)
            {
                finalSequence = match.Groups[1].Value;
                break;
            }
        }

        Assert.NotNull(finalSequence);
        Assert.Equal("0123456789", finalSequence);
    }

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task Two_handlers_across_multiple_rounds_never_reorder(LiveTransportKind transport)
    {
        // Tighter loop: 50 rounds × 2 handlers (h0 then h1). The mutation
        // pattern means the only way Sequence ends in something other than
        // "01010101…" alternating is if a later h1 dispatched before its
        // paired h0. With chained dispatch the sequence is always strictly
        // alternating in arrival order.
        using var stress = new ThreadPoolStress(8);

        using var host = RaskTestHost.Create<OrderedDispatchApp>(diffMode: LiveDiffMode.DisabledFull);
        var initialHtml = await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerIds = ExtractAllHandlerIds(initialHtml);

        await using var ws = await LiveTestConnection.OpenAsync(host, transport, sessionId);

        const int rounds = 50;
        var expected = string.Concat(Enumerable.Repeat("01", rounds));
        for (var i = 0; i < rounds; i++)
        {
            await ws.SendJsonAsync(new { id = handlerIds[0] });
            await ws.SendJsonAsync(new { id = handlerIds[1] });
        }

        string? finalSequence = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
            if (text is null)
            {
                continue;
            }

            var match = Regex.Match(text, "Sequence=([0-9]*)");
            if (match.Success && match.Groups[1].Value.Length == expected.Length)
            {
                finalSequence = match.Groups[1].Value;
                break;
            }
        }

        Assert.NotNull(finalSequence);
        Assert.Equal(expected, finalSequence);
    }

    private static List<string> ExtractAllHandlerIds(string html) =>
        Regex.Matches(html, "data-rask-on-click=\"(h\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

    // Spawns N background workers that hammer the ThreadPool with short
    // compute work, raising the chance that handler-dispatch continuations
    // get scheduled out of order if the framework's chaining is broken.
    private sealed class ThreadPoolStress : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _tasks;

        public ThreadPoolStress(int workers)
        {
            _tasks = new Task[workers];
            for (var i = 0; i < workers; i++)
            {
                _tasks[i] = Task.Run(async () =>
                {
                    var rng = new Random();
                    while (!_cts.IsCancellationRequested)
                    {
                        // Pure compute keeps a worker busy without grabbing locks.
                        var n = 0;
                        for (var k = 0; k < 5000; k++)
                        {
                            n = unchecked(n + rng.Next());
                        }

                        await Task.Yield();
                    }
                }, _cts.Token);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { Task.WaitAll(_tasks, TimeSpan.FromSeconds(2)); }
            catch { }

            _cts.Dispose();
        }
    }
}
