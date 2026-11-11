using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server;
using Rask.Server.Diagnostics;
using Rask.Server.Hibernation;

namespace Rask.Benchmarks;

/// <summary>
///     What hibernating a hidden tab's session buys and costs (#1322) — a one-shot report, not a BenchmarkDotNet run.
///     <para>
///         <b>Freed per session</b>: connected sessions at steady state are offered hibernation (the offer seals the
///         record and sends it), then let go through the store's removal funnel exactly as an agreed hibernation is;
///         the heap's live bytes before and after, per session. <b>Record</b>: the bytes the tab holds instead,
///         base64 as it travels — the page's address and its declared state (none here, so the floor).
///     </para>
///     <para>
///         <b>Rebuild</b>: the server's side of waking — opening the record, building a session for it, restoring the
///         declared state, and rendering the page — timed per session, p50 and p99. It leaves out the transport and
///         the route guard; the Server E2E journey prints the browser's whole wait, shown to page back.
///     </para>
///     <para>Invoke: <c>dotnet run -c Release --project tests/Rask.Benchmarks -- session-hibernation</c> (<c>--smoke</c> proves it runs).</para>
/// </summary>
internal static class SessionHibernationReport
{
    private const int DefaultSessions = 200;

    private static readonly (string Name, int Rows)[] Pages =
    [
        ("Empty_0Rows", 0),
        ("Small_5Rows", 5),
        ("Medium_200Rows", 200),
        ("Large_1000Rows", 1000)
    ];

    public static int Run(string[] args)
    {
        var smoke = Array.IndexOf(args, "--smoke") >= 0;
        var sessions = smoke ? 10 : DefaultSessions;
        var pages = smoke ? Pages[..2] : Pages;
        SessionHarness.VerifySelfMeasurement();

        Console.WriteLine("# Live-session hibernation: memory a hidden tab's session frees, the record the tab holds");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "# instead, and the server's cost to rebuild the page from it. {0} sessions per row.", sessions));
        Console.WriteLine("Page,FreedBytesPerSession,RecordBytes,RebuildP50Ms,RebuildP99Ms");
        foreach (var (name, rows) in pages)
        {
            var (freed, recordBytes, records) = Hibernate(rows, sessions);
            var (p50, p99) = Rebuild(rows, records);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:0.000},{4:0.000}",
                name, freed, recordBytes, p50, p99));
        }

        return 0;
    }

    private static ServiceProvider NewHost() =>
        new ServiceCollection()
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .AddRask()
            .BuildServiceProvider();

    private static (long Freed, int RecordBytes, List<string> Records) Hibernate(int rows, int sessions)
    {
        var services = NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();
        var protector = services.GetRequiredService<SessionResumeSupport>().Protector
                        ?? throw new InvalidOperationException("The host seals no resume records, so nothing can hibernate.");
        SessionHarness.Create(store, rows, connected: true);

        var ids = BuildConnected(store, rows, sessions);
        var before = SessionHarness.StableHeap();
        var records = HibernateAll(store, protector, ids);
        var after = SessionHarness.StableHeap();
        GC.KeepAlive(store);
        GC.KeepAlive(services);

        return ((before - after) / sessions, records[0].Length, records);
    }

    private static List<string> BuildConnected(LiveSessionStore store, int rows, int sessions)
    {
        var ids = new List<string>(sessions);
        for (var i = 0; i < sessions; i++)
        {
            var handle = SessionHarness.Create(store, rows, connected: true);
            SessionHarness.EnsureReachedSteadyState(handle);
            ids.Add(handle.Session.Id);
        }

        return ids;
    }

    // What an agreed hibernation does: the offer (record sealed and sent), then the removal its close leads to.
    private static List<string> HibernateAll(LiveSessionStore store, SessionHandoffProtector protector, List<string> ids)
    {
        var records = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            var session = store.Peek(id)!;
            if (!session.OfferHibernationAsync(store.Clock.GetTimestamp(), HibernationTrigger.Hidden).GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("A settled, connected session was not offered hibernation.");
            }

            var route = session.Services.GetRequiredService<Rask.Core.Routing.RouteState>();
            records.Add(protector.Protect(
                Rask.Core.Routing.QueryString.Build(route.Path, route.Query), user: null,
                session.Services.GetRequiredService<PersistentState>().Entries));
            store.RemoveAsync(id, SessionRemoval.Hibernated).GetAwaiter().GetResult();
        }

        return records;
    }

    private static (double P50, double P99) Rebuild(int rows, List<string> records)
    {
        var services = NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();
        var protector = services.GetRequiredService<SessionResumeSupport>().Protector!;
        var times = new List<double>(records.Count);
        RebuildOne(store, protector, rows, records[0]);
        foreach (var record in records)
        {
            var clock = Stopwatch.StartNew();
            RebuildOne(store, protector, rows, record);
            times.Add(clock.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        return (times[times.Count / 2], times[Math.Min(times.Count - 1, (int)(times.Count * 0.99))]);
    }

    // The server's side of a wake: the record opened for its user, a session built and its state restored before the
    // first render, and that render.
    private static void RebuildOne(LiveSessionStore store, SessionHandoffProtector protector, int rows, string record)
    {
        if (!protector.TryUnprotect(record, user: null, out var opened, out _))
        {
            throw new InvalidOperationException("A record sealed a moment ago did not open.");
        }

        var session = store.Create(_ =>
        {
#pragma warning disable RASK014 // the benchmark mirrors the GET endpoint's implicit boundary, which has no chain entry
            return new RootErrorBoundary(FootprintApp.RowCount(rows));
#pragma warning restore RASK014
        });
        session.Services.GetRequiredService<PersistentState>().Restore(opened.Entries);
        _ = session.RenderInitialRoot();
        store.RemoveAsync(session.Id).GetAwaiter().GetResult();
    }
}
