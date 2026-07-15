using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Server;

namespace Rask.Benchmarks;

/// <summary>
///     Answers "how many concurrent live sessions fit in 1 GB" — a one-shot report (NOT a
///     BenchmarkDotNet benchmark), measured from the GC's per-generation live-bytes counters.
///     <para>
///         <b>Why a sweep and not one number.</b> A session's retained cost is dominated by things that
///         scale with the <i>page</i>, not with the update: two <c>RenderedHtmlBuffers</c> char arrays
///         (UTF-16 — 4 bytes of RAM per page character across the pair), the <c>SessionRenderCache</c>'s
///         two <c>FrameWriter</c>s (~40 B per node), <c>_writeBuffer</c>/<c>_lastSentBuffer</c>, and —
///         usually the largest term — the retained Element graph. All of them grow to a high-water mark
///         and never shrink. The answer therefore swings by two orders of magnitude across realistic
///         pages, and a single headline figure would be wrong for almost everyone.
///     </para>
///     <para>
///         <b>Two axes.</b> <c>Rows</c> sweeps page size with the page's shape held constant, so the delta
///         is attributable to size alone; <c>Empty_0Rows</c> isolates the fixed per-session floor that no
///         page can go below. <c>State</c> separates a session created by a bare GET whose socket never
///         arrived — it still holds a <c>MaxSessions</c> slot for the 10 s
///         <c>UnconnectedSessionGracePeriod</c>, which makes it the cheapest session an attacker can mint —
///         from one with an attached socket and updates behind it, where every buffer pair sits at its
///         high-water mark.
///     </para>
///     <para>
///         <b>On the page shape.</b> Rows are keyed and each owns a handler, i.e. a real data grid. That is
///         deliberately the shape the clean-subtree cache cannot help: <c>TryCacheCleanSubtree</c> rejects
///         any component carrying a <c>Key</c>, and RASK022 pushes every list item toward one — so the
///         pages where retained memory actually matters are exactly the pages that keep their Element
///         graph. A keyless, handler-free variant was measured during development and did not come out
///         materially cheaper per byte of page, so the sweep reports the realistic shape only rather than
///         an axis whose arms differ in page size as well as in shape.
///     </para>
///     <para>
///         <b>What this excludes.</b> The transport (a real socket plus Kestrel's ~32 KB of per-connection
///         buffers) and the application's own scoped services. A per-session DbContext dwarfs everything
///         measured here. This is the framework's floor, not a budget for a real app.
///     </para>
///     <para>
///         Invoke: <c>dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-footprint</c>
///         (optionally <c>--sessions=400</c> to confirm the number is stable in N).
///     </para>
/// </summary>
internal static class SessionFootprintReport
{
    private const int DefaultSessions = 200;
    private const long BytesPerGib = 1024L * 1024 * 1024;

    private static readonly (string Name, int Rows)[] Pages =
    [
        ("Empty_0Rows", 0),
        ("Small_5Rows", 5),
        ("Medium_200Rows", 200),
        ("Large_1000Rows", 1000)
    ];

    public static int Run(string[] args)
    {
        var sessions = ParseSessions(args);
        SessionHarness.VerifySelfMeasurement();

        Console.WriteLine("# Live-session retained footprint. Framework floor only — excludes the transport");
        Console.WriteLine("# (real socket + Kestrel per-connection buffers) and the app's own scoped services.");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "# {0} sessions retained per row; SessionsPer1GiB = 1GiB / BytesPerSession.", sessions));
        Console.WriteLine("# Page shape: a keyed data-table row owning one event handler — a real data grid,");
        Console.WriteLine("# and the shape the clean-subtree cache cannot help (a Key alone disqualifies it).");
        Console.WriteLine("# State: Unconnected = GET'd, socket never attached (still holds a MaxSessions slot);");
        Console.WriteLine("#        Connected = socket attached + updates driven (buffers at high-water).");
        Console.WriteLine("Page,PageHtmlBytes,State,BytesPerSession,SessionsPer1GiB");

        foreach (var (name, rows) in Pages)
        {
            // A zero-row page has no rows to make interactive, so the shape axis is meaningless there —
            // emitting it twice would imply a comparison that isn't being made.
            Emit(name, rows, connected: false, sessions);
            Emit(name, rows, connected: true, sessions);
        }

        return 0;
    }

    private static void Emit(string name, int rows, bool connected, int sessions)
    {
        var (bytesPerSession, pageHtmlBytes) = Measure(rows, connected, sessions);
        var density = bytesPerSession <= 0
            ? "n/a"
            : (BytesPerGib / bytesPerSession).ToString(CultureInfo.InvariantCulture);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
            name, pageHtmlBytes, connected ? "Connected" : "Unconnected", bytesPerSession, density));
    }

    private static (long BytesPerSession, int PageHtmlBytes) Measure(int rows, bool connected, int sessions)
    {
        var services = SessionHarness.NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();

        // Warm up on the same store: settles JIT, first-use statics, and the ArrayPool bucket cache
        // before the baseline is taken. The warm-up session stays in the store, so it lands inside
        // `before` and doesn't skew the delta.
        var warmup = SessionHarness.Create(store, rows, connected);
        if (connected)
        {
            SessionHarness.EnsureReachedSteadyState(warmup);
        }

        var before = SessionHarness.StableHeap();
        // Built in a separate method so its transient locals (per-session HTML strings, factory closures)
        // are out of scope and reclaimable before `after` is read — only what the store and the sessions
        // genuinely retain counts toward the delta.
        BuildSessionsInto(store, rows, connected, sessions);
        var after = SessionHarness.StableHeap();
        GC.KeepAlive(store);
        GC.KeepAlive(services);

        return ((after - before) / sessions, warmup.PageHtmlBytes);
    }

    private static void BuildSessionsInto(LiveSessionStore store, int rows, bool connected, int sessions)
    {
        for (var i = 0; i < sessions; i++)
        {
            SessionHarness.Create(store, rows, connected);
        }
    }

    private static int ParseSessions(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--sessions=", StringComparison.Ordinal)
                && int.TryParse(arg.AsSpan("--sessions=".Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var n)
                && n > 0)
            {
                return n;
            }
        }

        return DefaultSessions;
    }
}
