using System.Diagnostics;
using System.Globalization;
using Rask.Benchmarks.Infrastructure;

namespace Rask.Benchmarks;

/// <summary>
///     What a host does under real load: how many event round-trips per second it closes, how long each
///     one takes at the tail.
/// </summary>
/// <remarks>
///     <para>
///         The gap this fills: <c>session-footprint</c> and <c>session-churn</c> answer "how many sessions
///         fit" — a retained-memory question, measured against a stub socket that never receives. Neither
///         has ever opened a socket, so nothing until now said what happens when those sessions are
///         actually <em>used</em>. A capacity number you can't serve is not a capacity number.
///     </para>
///     <para>
///         Real Kestrel, real <c>ClientWebSocket</c>s (see <see cref="LoadHost" />), and the round trip is
///         timed the way the client experiences it: click to the <c>seq</c> ack that closes it, which is
///         the server saying it has finished with that event — including the renders it caused, or the
///         dedup that produced none.
///     </para>
///     <para>
///         Percentiles are computed over every recorded sample rather than a running estimate. A load run
///         holds a few hundred thousand doubles at most, and an exact p99 is worth more than the memory:
///         the tail is the whole point of the measurement.
///     </para>
///     <para>
///         <b>No memory column, deliberately.</b> An early version reported bytes-per-session here and the
///         number was not trustworthy: the load generator shares this process with the host it is driving,
///         so a heap delta counts the client sockets and their receive buffers alongside the sessions. It
///         showed as a figure that did not even rise with page size. Memory belongs to
///         <c>session-footprint</c>, which measures it against a stub socket with nothing else running.
///     </para>
/// </remarks>
internal static class SessionLoadReport
{
    // The same page sizes the footprint sweep uses, so a row here can be read against a row there. The
    // 1,000-row page is left out: at that size a single render dominates the round trip and the report
    // stops measuring the host and starts measuring one page's serializer cost.
    private static readonly int[] Pages = [0, 5, 200];

    private const int DefaultSessions = 50;
    private const int DefaultSeconds = 5;
    private const int WarmupSeconds = 2;

    public static int Run(string[] args)
    {
        // --smoke: prove the report still RUNS, on the gate's budget. ONE page, no sweep, and none of
        // the JIT throwaway below — a smoke is not a measurement, and the numbers it prints are not
        // comparable with a real run's. Left as a sweep it costs ~13s of every push (3 pages x warmup +
        // measure, plus 4 Kestrel starts) for an answer the gate never reads. See
        // scripts/run-benchmarks-local.sh, and session-churn --smoke for the one smoke that ASSERTS.
        var smoke = Array.IndexOf(args, "--smoke") >= 0;
        var sessions = IntArg(args, "--sessions=") ?? (smoke ? 2 : DefaultSessions);
        var seconds = IntArg(args, "--seconds=") ?? (smoke ? 1 : DefaultSeconds);
        var pages = smoke ? [5] : Pages;

        SessionHarness.VerifySelfMeasurement();

        Console.WriteLine();
        Console.WriteLine($"# Session load — {sessions} concurrent sessions per row, {seconds}s measured");
        Console.WriteLine("# after a " + WarmupSeconds + "s warmup, over real WebSockets against a real Kestrel host.");
        Console.WriteLine("#");
        Console.WriteLine("# Latency is one event round trip: click -> the seq ack that closes it. That");
        Console.WriteLine("# covers the render the click caused, so it is the number a user feels.");
        Console.WriteLine("# No memory column: the generator shares this process with the host, so a heap");
        Console.WriteLine("# delta would count the client sockets too. Use session-footprint for memory.");
        Console.WriteLine();
        Console.WriteLine("Rows,Sessions,EventsPerSecond,P50Ms,P95Ms,P99Ms,MaxMs,Errors");

        // One throwaway host+load before the sweep. Each row gets its own warmup, but the FIRST row also
        // pays the process-wide costs — JIT across Kestrel, the WebSocket stack and the render path — and
        // reported them as if they were the page's. It showed: the empty page came out slower than the
        // 5-row one, which is not a thing that can be true.
        if (!smoke)
        {
            RunOneAsync(rows: 5, sessions: Math.Min(4, sessions), seconds: 1).GetAwaiter().GetResult();
        }

        foreach (var rows in pages)
        {
            var result = RunOneAsync(rows, sessions, seconds).GetAwaiter().GetResult();
            Console.WriteLine(string.Join(',',
                rows.ToString(CultureInfo.InvariantCulture),
                sessions.ToString(CultureInfo.InvariantCulture),
                result.EventsPerSecond.ToString("F0", CultureInfo.InvariantCulture),
                result.P50.ToString("F2", CultureInfo.InvariantCulture),
                result.P95.ToString("F2", CultureInfo.InvariantCulture),
                result.P99.ToString("F2", CultureInfo.InvariantCulture),
                result.Max.ToString("F2", CultureInfo.InvariantCulture),
                result.Errors.ToString(CultureInfo.InvariantCulture)));
        }

        Console.WriteLine();
        return 0;
    }

    private static async Task<LoadResult> RunOneAsync(int rows, int sessions, int seconds)
    {
        await using var host = await LoadHost.StartAsync(rows);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = cts.Token;

        var clients = new List<LoadClient>(sessions);
        try
        {
            for (var i = 0; i < sessions; i++)
            {
                clients.Add(await host.ConnectAsync(ct));
            }

            // Warmup: JIT the dispatch and render paths, let each session's pooled buffers reach the
            // page's high-water mark, and let the diff baseline settle. Measuring through this would
            // report first-render costs the steady state never pays again.
            await DriveAsync(clients, TimeSpan.FromSeconds(WarmupSeconds), null, ct);

            var samples = new List<double>(sessions * seconds * 64);
            var started = Stopwatch.GetTimestamp();
            var errors = await DriveAsync(clients, TimeSpan.FromSeconds(seconds), samples, ct);
            var elapsed = Stopwatch.GetElapsedTime(started);

            samples.Sort();
            return new LoadResult(
                samples.Count / elapsed.TotalSeconds,
                Percentile(samples, 0.50),
                Percentile(samples, 0.95),
                Percentile(samples, 0.99),
                samples.Count == 0 ? 0 : samples[^1],
                errors);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    ///     Drives every client in a closed loop for <paramref name="duration" />: each fires its next event
    ///     as soon as the previous one is acknowledged.
    /// </summary>
    /// <remarks>
    ///     Closed-loop on purpose. An open-loop generator firing at a fixed rate measures how far behind a
    ///     saturated host falls, which needs a target rate chosen in advance and reports queue depth
    ///     dressed up as latency. Closed-loop asks the question a live app actually poses — one user, one
    ///     interaction at a time, as fast as the server answers — so throughput and latency stay honest
    ///     with each other.
    /// </remarks>
    private static async Task<int> DriveAsync(
        List<LoadClient> clients, TimeSpan duration, List<double>? samples, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var errors = 0;
        var seq = 0L;
        var gate = new object();

        var workers = clients.Select(client => Task.Run(async () =>
        {
            while (Stopwatch.GetTimestamp() < deadline && !ct.IsCancellationRequested)
            {
                var mine = Interlocked.Increment(ref seq);
                var started = Stopwatch.GetTimestamp();
                try
                {
                    if (!await client.ClickAndAwaitAckAsync(mine, ct))
                    {
                        Interlocked.Increment(ref errors);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                    return;
                }

                if (samples is null)
                {
                    continue;
                }

                var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                lock (gate)
                {
                    samples.Add(ms);
                }
            }
        }, ct)).ToArray();

        await Task.WhenAll(workers);
        return errors;
    }

    /// <summary>Nearest-rank percentile over the sorted samples — exact, not interpolated or estimated.</summary>
    private static double Percentile(List<double> sorted, double q)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static int? IntArg(string[] args, string prefix)
    {
        var match = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return match is not null && int.TryParse(match[prefix.Length..], CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private readonly record struct LoadResult(
        double EventsPerSecond,
        double P50,
        double P95,
        double P99,
        double Max,
        int Errors);
}
