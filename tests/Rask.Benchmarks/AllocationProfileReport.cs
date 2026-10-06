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
