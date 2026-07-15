using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Server;

namespace Rask.Benchmarks;

/// <summary>
///     The stress counterpart to <see cref="SessionFootprintReport" />. That report measures a session's
///     footprint at one instant; this one asks whether that footprint <i>holds</i> — because a capacity
///     number is only meaningful if per-session cost converges rather than creeping upward with traffic.
///     A one-shot report (NOT a BenchmarkDotNet benchmark).
///     <para>
///         <b>Soak pass.</b> Holds a fixed set of sessions open and drives update rounds through them,
///         reporting retained bytes-per-session after each round. The framework's buffers are all
///         grow-to-high-water-and-reuse, so the curve <b>should go flat</b> once the buffers have sized
///         themselves to the page. A curve that keeps climbing means something accumulates per update —
///         the root's handler map, the alive-sets, or the keyed-diff scratch retaining capacity — and the
///         capacity number would then be a function of uptime rather than of page size.
///     </para>
///     <para>
///         <b>Churn pass.</b> Creates, renders and disposes sessions in a loop. Read
///         <c>RetainedTotal</c>, not the per-cycle column: a leak makes the TOTAL climb with the cycle
///         count, whereas a constant total (with the per-cycle figure decaying toward zero as it is
///         divided by more cycles) means nothing survives teardown.
///     </para>
///     <para>
///         <c>AllocBytesPerCycle</c> is the turnover cost — what the server allocates to stand a session
///         up and tear it down. Worth watching because <c>LiveSession.Dispose</c> never disposes
///         <c>_htmlBuffers</c> or the <c>SessionRenderCache</c>, both of which have a <c>Dispose</c> that
///         would return their pooled <c>char[]</c>/<c>RenderFrame[]</c> rentals to the
///         <c>ArrayPool</c>. Nothing leaks — unreturned arrays are simply collected — but the pool never
///         gets them back, so each new session re-allocates buffers a departing one could have handed
///         over. That is a churn cost, not a retention cost, which is exactly why it surfaces here and
///         not in the footprint report.
///     </para>
///     <para>
///         <b>Update-cost pass.</b> Allocation and wall time for one state change on a steady-state
///         session. The footprint report's retained bytes set the capacity ceiling; this is what a user
///         pays per interaction. A change that trades retained memory for update cost (or the reverse)
///         has to be read against both.
///     </para>
///     <para>
///         Invoke: <c>dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-churn</c>
///     </para>
/// </summary>
internal static class SessionChurnReport
{
    private const int SoakSessions = 100;
    private const int SoakRounds = 8;
    private const int UpdatesPerRound = 25;
    private const int ChurnCycles = 500;
    private const int ChurnBatch = 50;
    private const int UpdateWarmup = 200;
    private const int UpdateSamples = 2000;

    private static readonly int[] UpdateCostRows = [200, 1000];

    // The medium page from the footprint sweep — big enough that the page-scaled buffers dominate,
    // small enough to soak quickly.
    private const int Rows = 200;

    public static int Run(string[] args)
    {
        _ = args;
        SessionHarness.VerifySelfMeasurement();
        Soak();
        Console.WriteLine();
        UpdateCost();
        Console.WriteLine();
        Churn();
        return 0;
    }

    // ---- Soak: does per-session cost converge? ---------------------------------------

    private static void Soak()
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "# Soak — {0} sessions held open, {1} updates each per round ({2}-row page).",
            SoakSessions, UpdatesPerRound, Rows));
        Console.WriteLine("# BytesPerSession should FLATTEN: the buffers grow to the page's high-water mark");
        Console.WriteLine("# and are then reused. A rising curve means per-update accumulation.");
        Console.WriteLine("Round,CumulativeUpdatesPerSession,BytesPerSession,DeltaVsPreviousRound");

        var services = SessionHarness.NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();

        var baseline = SessionHarness.StableHeap();
        var sessions = new List<SessionHarness.SessionHandle>(SoakSessions);
        BuildSoakSet(store, sessions);
        SessionHarness.EnsureReachedSteadyState(sessions[0]);

        long previous = 0;
        for (var round = 1; round <= SoakRounds; round++)
        {
            foreach (var handle in sessions)
            {
                SessionHarness.Drive(handle.Session, handle.App, UpdatesPerRound);
            }

            var perSession = (SessionHarness.StableHeap() - baseline) / SoakSessions;
            var delta = round == 1 ? 0 : perSession - previous;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                round, SessionHarness.UpdatesToSteadyState + (round * UpdatesPerRound), perSession,
                round == 1 ? "-" : delta.ToString("+#;-#;0", CultureInfo.InvariantCulture)));
            previous = perSession;
        }

        GC.KeepAlive(sessions);
        GC.KeepAlive(store);
        GC.KeepAlive(services);
    }

    // Separate method so the per-session transients fall out of scope before the rounds are measured.
    private static void BuildSoakSet(LiveSessionStore store, List<SessionHarness.SessionHandle> sessions)
    {
        for (var i = 0; i < SoakSessions; i++)
        {
            sessions.Add(SessionHarness.Create(store, Rows, connected: true));
        }
    }

    // ---- Update cost: what one interaction costs -------------------------------------

    // The footprint report answers what a session RETAINS; this answers what it costs to USE. Retained
    // bytes set the capacity ceiling, but allocation per update is what turns into GC pressure while a
    // user is clicking, so a change that trades one for the other has to show both.
    private static void UpdateCost()
    {
        Console.WriteLine("# Update cost — allocation and wall time for ONE state change on a live session,");
        Console.WriteLine("# measured after warmup on a steady-state session. This is the per-interaction cost");
        Console.WriteLine("# (GC pressure), as opposed to the retained ceiling session-footprint reports.");
        Console.WriteLine("Rows,AllocBytesPerUpdate,MicrosecondsPerUpdate");

        foreach (var rows in UpdateCostRows)
        {
            var services = SessionHarness.NewHost();
            var store = services.GetRequiredService<LiveSessionStore>();
            var handle = SessionHarness.Create(store, rows, connected: true);
            SessionHarness.EnsureReachedSteadyState(handle);
            SessionHarness.Drive(handle.Session, handle.App, UpdateWarmup);

            var before = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            SessionHarness.Drive(handle.Session, handle.App, UpdateSamples);
            sw.Stop();

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:0.0}",
                rows, (GC.GetAllocatedBytesForCurrentThread() - before) / UpdateSamples,
                sw.Elapsed.TotalMicroseconds / UpdateSamples));
            GC.KeepAlive(store);
            GC.KeepAlive(services);
        }
    }

    // ---- Churn: does a session survive its own disposal? ------------------------------

    private static void Churn()
    {
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "# Churn — {0} create→render→dispose cycles ({1}-row page), reported every {2}.",
            ChurnCycles, Rows, ChurnBatch));
        Console.WriteLine("# RetainedTotal is the number to read: FLAT across cycle counts = nothing survives");
        Console.WriteLine("# teardown. (PerCycle is RetainedTotal/Cycles, so it decays toward 0 when total is");
        Console.WriteLine("# flat — a real leak would hold PerCycle steady and grow the total instead.)");
        Console.WriteLine("Cycles,RetainedTotal,RetainedPerCycle,AllocBytesPerCycle");

        var services = SessionHarness.NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();

        // Warm up so JIT/statics/pool are settled before the baseline.
        RunChurnBatch(store, ChurnBatch);

        var baseline = SessionHarness.StableHeap();
        var allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var done = 0;
        while (done < ChurnCycles)
        {
            RunChurnBatch(store, ChurnBatch);
            done += ChurnBatch;

            var retainedTotal = SessionHarness.StableHeap() - baseline;
            var allocPerCycle = (GC.GetAllocatedBytesForCurrentThread() - allocBefore) / done;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}",
                done, retainedTotal, retainedTotal / done, allocPerCycle));
        }

        GC.KeepAlive(store);
        GC.KeepAlive(services);
    }

    // Each cycle mints a session, drives it to steady state, then disposes it — which also removes it
    // from the store, so a leak here is the session itself outliving its own teardown.
    private static void RunChurnBatch(LiveSessionStore store, int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            SessionHarness.Remove(store, SessionHarness.Create(store, Rows, connected: true));
        }
    }
}
