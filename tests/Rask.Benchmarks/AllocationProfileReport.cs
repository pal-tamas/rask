using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Server;

namespace Rask.Benchmarks;

/// <summary>
///     Which types a live update, or a page request, allocates — the question <c>Allocated</c> raises and
///     cannot answer.
/// </summary>
/// <remarks>
///     Drives the page <see cref="LiveSessionSendBenchmarks" /> measures and histograms the runtime's
///     allocation ticks, which fire about once per 100 KB allocated and name the type that crossed the line.
///     A share here is a share of bytes, so it reads straight against the benchmark's number.
/// </remarks>
internal static class AllocationProfileReport
{
    private const int LiveUpdates = 200_000;
    private const int PageRequests = 20_000;

    public static int Run(string[] args)
    {
        if (args.Contains("--check", StringComparer.Ordinal))
        {
            return CheckLiveUpdate();
        }

        if (args.Length > 1 && string.Equals(args[1], "page", StringComparison.Ordinal))
        {
            return ProfilePageRequest();
        }

        return ProfileLiveUpdate(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 20);
    }

    private static int ProfileLiveUpdate(int rows)
    {
        var services = SessionHarness.NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();
        var handle = SessionHarness.Create(store, rows, connected: true);
        SessionHarness.Drive(handle.Session, handle.App, 2_000);

        Profile($"{rows} rows, live update", LiveUpdates, () => SessionHarness.Drive(handle.Session, handle.App, LiveUpdates));

        SessionHarness.Remove(store, handle);
        services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }

    // The gate: a live update of the 20-row page may not allocate more than the committed budget allows.
    // Bytes, not time, so a shared runner answers the same as a quiet machine. The budget is the count
    // with tiered PGO off (scripts/run-benchmarks-local.sh sets DOTNET_TieredPGO=0 on this process): with
    // it on, the measured window straddles the tier-up and the count wanders by a few dozen bytes.
    private static int CheckLiveUpdate()
    {
        const int rows = 20;
        const int updates = 20_000;
        const double headroom = 1.05;
        var budget = long.Parse(
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Baselines", "allocation-budget.csv"))
                .First(line => line.StartsWith("LiveUpdate20Rows,", StringComparison.Ordinal))
                .Split(',')[1],
            CultureInfo.InvariantCulture);

        var services = SessionHarness.NewHost();
        var store = services.GetRequiredService<LiveSessionStore>();
        var handle = SessionHarness.Create(store, rows, connected: true);
        SessionHarness.Drive(handle.Session, handle.App, 2_000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        SessionHarness.Drive(handle.Session, handle.App, updates);
        var each = (GC.GetAllocatedBytesForCurrentThread() - before) / updates;
        SessionHarness.Remove(store, handle);
        services.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var ok = each <= budget * headroom;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  LiveUpdate20Rows       {each,6} B per update (budget {budget} B, +5% allowed)  [{(ok ? "ok" : "REGRESSION")}]"));
        if (each < budget / headroom)
        {
            Console.WriteLine("  An improvement: lower the budget in Baselines/allocation-budget.csv to keep it.");
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_TieredPGO"), "0", StringComparison.Ordinal))
        {
            Console.WriteLine("  Tiered PGO is on, so this count wanders from run to run. The budget is the exact count with DOTNET_TieredPGO=0.");
        }

        return ok ? 0 : 1;
    }

    // The page PageRequestBenchmarks measures, through the same in-memory host.
    private static int ProfilePageRequest()
    {
        var page = new PageRequestBenchmarks();
        page.Setup();
        for (var i = 0; i < 500; i++)
        {
            page.GetPage().GetAwaiter().GetResult();
        }

        Profile("page request", PageRequests, () =>
        {
            for (var i = 0; i < PageRequests; i++)
            {
                page.GetPage().GetAwaiter().GetResult();
            }
        });

        page.Cleanup();
        return 0;
    }

    private static void Profile(string what, int iterations, Action run)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        using var ticks = new AllocationTicks();
        run();
        var each = (GC.GetTotalAllocatedBytes(precise: true) - before) / iterations;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"# {what}: {each} B each"));
        ticks.Print(each);
    }

    private sealed class AllocationTicks : EventListener
    {
        private const EventKeywords Gc = (EventKeywords)0x1;
        private readonly Dictionary<string, int> _byType = new(StringComparer.Ordinal);

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (string.Equals(eventSource.Name, "Microsoft-Windows-DotNETRuntime", StringComparison.Ordinal))
            {
                EnableEvents(eventSource, EventLevel.Verbose, Gc);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true
                || eventData.PayloadNames is not { } names || eventData.Payload is not { } payload)
            {
                return;
            }

            var type = payload[names.IndexOf("TypeName")] as string ?? "?";
            lock (_byType)
            {
                _byType[type] = _byType.GetValueOrDefault(type) + 1;
            }
        }

        public void Print(long perUpdate)
        {
            lock (_byType)
            {
                var total = _byType.Values.Sum();
                foreach (var (type, count) in _byType.OrderByDescending(pair => pair.Value).Take(25))
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{100.0 * count / total,5:F1}%  ~{perUpdate * count / total,5} B  {type}"));
                }
            }
        }
    }
}
