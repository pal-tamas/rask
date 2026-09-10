using Rask.Benchmarks.Sqlite;
using Rask.Benchmarks.Sqlite.Scenarios;

// A load harness for Rask.SQLite and Rask.SQLite.EntityFrameworkCore: sustained concurrent load, reported as
// throughput, tail latency and error rates. Deliberately not BenchmarkDotNet — BDN measures a burst's mean
// well, but gives no percentiles, no error rates and no sustained-throughput number, which is the whole
// question here. The BDN suite next door (Rask.Benchmarks/SqliteWriteContentionBenchmarks) answers the
// different question of per-operation cost.

if (args.Length == 0)
{
    Console.Error.WriteLine(
        """
        usage: <workload> [options]

        workloads:
          write   write contention: raw-nonblocking | raw-native-busytimeout | ef-retry | ef-no-retry
          read    read-under-write: wal-readers-only | wal-read-under-write | delete-read-under-write
          mixed   ~90/10 read/write web traffic over 10k seeded rows: mixed-raw | mixed-ef
          soak    mixed traffic held for minutes: soak-mixed | soak-wal-pinned
          split   app writes under battery churn, one file or three: {one-file,split}-{idle,busy}
          all     write + read + mixed + split
          check   the regression gate (invariants and same-run ratios; never absolute ms)

        options:
          --vus 1,8,32       virtual-user levels to sweep   (default 1,4,8,16,32,64,128,256)
          --duration 15|90s|5m   measured duration per level (default 15s)
          --warmup 5         warmup, discarded              (default 5s)
          --window 5         window size for long runs      (default 5s)
          --writers 1        concurrent writers (read workload only)
          --out <path>       write the CSV to a file
          --ci               gate: hardware-independent invariants only
        """);
    return 1;
}

var options = LoadOptions.Parse(args);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Teardown must run on Ctrl-C too, or a cancelled soak strands a multi-gigabyte WAL in the temp dir.
    e.Cancel = true;
    cts.Cancel();
};

var results = new List<LoadResult>();

switch (args[0])
{
    case "write":
        await RunSweepAsync("write", WriteScenarios.All);
        break;
    case "read":
        await RunSweepAsync("read", ReadScenarios.For(options.Writers));
        break;
    case "mixed":
        await RunSweepAsync("mixed", MixedScenarios.All);
        break;
    case "soak":
        await RunSweepAsync("soak", MixedScenarios.Soak);
        break;
    case "split":
        await RunSweepAsync("split", SplitStoreScenarios.All);
        break;
    case "all":
        await RunSweepAsync("write", WriteScenarios.All);
        await RunSweepAsync("read", ReadScenarios.For(options.Writers));
        await RunSweepAsync("mixed", MixedScenarios.All);
        await RunSweepAsync("split", SplitStoreScenarios.All);
        break;
    case "check":
        return await LoadGate.RunAsync(options, cts.Token);
    default:
        Console.Error.WriteLine($"Unknown workload '{args[0]}'.");
        return 1;
}

LoadReport.Print(results);

if (options.OutPath is { } outPath)
{
    await File.WriteAllTextAsync(outPath, LoadReport.ToCsv(results), cts.Token);
    Console.WriteLine($"CSV → {outPath}");
}

return 0;

async Task RunSweepAsync(string workload, IReadOnlyList<Func<LoadScenario>> arms)
{
    // Arms run serially: SqliteConnection.ClearAllPools() in teardown is process-global, so a concurrent arm
    // would have its pool cleared out from under it.
    foreach (var vus in options.Vus)
    {
        foreach (var arm in arms)
        {
            var scenario = arm();
            Console.Error.WriteLine($"[{workload}] {scenario.Name} @ {vus} VUs ...");
            var result = await LoadRunner.RunAsync(scenario, workload, vus, options, cts.Token);
            results.Add(result);
        }
    }
}
