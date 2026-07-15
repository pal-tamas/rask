using System.Globalization;
using Rask.Benchmarks.Sqlite.Scenarios;

namespace Rask.Benchmarks.Sqlite;

/// <summary>One asserted property of the SQLite write/read paths.</summary>
internal sealed record Invariant(string Name, bool Passed, string Detail, bool Tier1);

/// <summary>
/// The regression gate. It deliberately asserts <b>invariants and same-run ratios</b>, never absolute
/// milliseconds or throughput.
/// <para>
/// That is not timidity, it is this repo's existing position: <c>ci.yml</c>'s benchmark job gates the
/// deterministic wire-byte reports and explicitly leaves the timing suites to nightly as "too noisy on
/// shared runners to gate on", and the mem-footprint report is logged rather than gated for the same reason.
/// A gate that flaked on a busy runner would be switched off within a week, which is worse than no gate.
/// </para>
/// <para>
/// The trick that makes the ratios portable: every comparison is between two arms measured in the same
/// process, on the same box, in the same run. A 2-vCPU runner and an M4 disagree wildly about absolute
/// milliseconds and agree completely that a DELETE-mode reader is far slower than a WAL-mode one.
/// </para>
/// <para>
/// Be clear about what this does <b>not</b> catch: a 20% throughput regression. Nothing cheap does, on
/// shared hardware. It catches the retry loop breaking, writes being lost, WAL ceasing to do its job, and
/// the EF retry switch silently disconnecting — which is the set of things that actually break here.
/// </para>
/// </summary>
internal static class LoadGate
{
    private const int LowVus = 32;
    private const int HighVus = 256;

    internal static async Task<int> RunAsync(LoadOptions options, CancellationToken cancellationToken)
    {
        // A short, fixed profile: the gate is a canary, not a measurement.
        var profile = options with
        {
            Vus = [LowVus, HighVus],
            Duration = TimeSpan.FromSeconds(10),
            Warmup = TimeSpan.FromSeconds(3),
        };

        var results = new List<LoadResult>();
        foreach (var vus in profile.Vus)
        {
            foreach (var arm in WriteScenarios.All)
            {
                var scenario = arm();
                Console.Error.WriteLine($"[check] {scenario.Name} @ {vus} VUs ...");
                results.Add(await LoadRunner.RunAsync(scenario, "write", vus, profile, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        foreach (var arm in ReadScenarios.For(writers: 1))
        {
            var scenario = arm();
            Console.Error.WriteLine($"[check] {scenario.Name} @ {LowVus} VUs ...");
            results.Add(await LoadRunner.RunAsync(scenario, "read", LowVus, profile, cancellationToken)
                .ConfigureAwait(false));
        }

        LoadReport.Print(results);

        var invariants = Evaluate(results, options.Ci);
        return Report(invariants, options.Ci);
    }

    private static List<Invariant> Evaluate(IReadOnlyList<LoadResult> results, bool ci)
    {
        var invariants = new List<Invariant>();
        LoadResult Arm(string name, int vus) => results.Single(r => r.Arm == name && r.Vus == vus);

        // 1. A retrying path must never surface SQLITE_BUSY. Both retry loops have a generous 30s timeout, so
        //    this holds on a loaded CI box too — if it fails, the retry itself is broken.
        foreach (var name in new[] { "raw-nonblocking", "ef-retry" })
        {
            foreach (var vus in new[] { LowVus, HighVus })
            {
                var arm = Arm(name, vus);
                invariants.Add(new Invariant(
                    $"{name} @ {vus}: no escaped SQLITE_BUSY",
                    arm.Overall.BusyErrors == 0,
                    $"{arm.Overall.BusyErrors} escaped",
                    Tier1: true));
            }
        }

        // 2. Every acknowledged commit is in the database. The existing stress test proves this for a burst;
        //    this proves it for sustained load.
        foreach (var arm in results.Where(r => r.LostWrites is not null))
        {
            invariants.Add(new Invariant(
                $"{arm.Arm} @ {arm.Vus}: no lost writes",
                arm.LostWrites == 0,
                $"{arm.LostWrites} acknowledged commits missing",
                Tier1: true));
        }

        // 3. Readers are not blocked by the writer — the headline WAL claim, as two same-run ratios.
        var baseline = Arm("wal-readers-only", LowVus).Overall.P99Ms;
        var underWrite = Arm("wal-read-under-write", LowVus).Overall.P99Ms;
        var deleteMode = Arm("delete-read-under-write", LowVus).Overall.P99Ms;

        invariants.Add(new Invariant(
            "WAL readers are not blocked by the writer",
            underWrite < 5 * baseline,
            $"p99 {underWrite:N2}ms under write vs {baseline:N2}ms idle (limit {5 * baseline:N2}ms)",
            Tier1: true));

        invariants.Add(new Invariant(
            "the rollback journal really is the slow one (control arm)",
            deleteMode > 3 * underWrite,
            $"DELETE p99 {deleteMode:N2}ms vs WAL {underWrite:N2}ms (needs > {3 * underWrite:N2}ms)",
            Tier1: true));

        // 4. The fair-interval retry's actual claim is a BOUNDED WORST CASE, and this is where it shows.
        //    Measured, the native-busy_timeout path is bimodal: most writers take the lock at once (so its
        //    p50 and even p99 beat the non-blocking path, and so does its throughput) while a few block for
        //    ten seconds. So the comparison has to be on the tail, not on p99 or ops/s — using p99 here would
        //    assert the opposite of the truth.
        //    This doubles as the canary: if the two write paths ever stop differing in the tail, they are no
        //    longer configured differently and every other number here is void.
        var nonBlocking = Arm("raw-nonblocking", HighVus).Overall;
        var native = Arm("raw-native-busytimeout", HighVus).Overall;
        invariants.Add(new Invariant(
            "the non-blocking retry bounds the worst case that busy_timeout does not",
            native.MaxMs > 3 * nonBlocking.MaxMs,
            $"native max {native.MaxMs:N0}ms vs non-blocking {nonBlocking.MaxMs:N0}ms " +
            $"(needs > {3 * nonBlocking.MaxMs:N0}ms)",
            Tier1: true));

        if (ci)
        {
            return invariants;
        }

        // 5. Catches "the CommandTimeout(1) lowering got dropped and a contended command blocks for the
        //    driver's 30s default again". An absolute bound, but a timeout-shaped one with a wide margin —
        //    it is not trying to detect a slow box, only a config regression an order of magnitude away.
        var efMax = Arm("ef-retry", HighVus).Overall.MaxMs;
        invariants.Add(new Invariant(
            "ef-retry never blocks for the driver's default command timeout",
            efMax < 15_000,
            $"max {efMax:N0}ms (limit 15,000ms; the un-lowered driver default would be ~30,000ms)",
            Tier1: false));

        return invariants;
    }

    private static int Report(List<Invariant> invariants, bool ci)
    {
        Console.WriteLine();
        Console.WriteLine(ci
            ? "Gate (Tier 1 — hardware-independent invariants only):"
            : "Gate (Tier 1 invariants + Tier 2 checks that need a real box):");

        foreach (var invariant in invariants)
        {
            var tier = invariant.Tier1 ? "T1" : "T2";
            Console.WriteLine($"  [{(invariant.Passed ? "ok" : "FAIL")}] {tier} {invariant.Name} — {invariant.Detail}");
        }

        var failed = invariants.Where(i => !i.Passed).ToArray();
        Console.WriteLine();

        if (failed.Length == 0)
        {
            Console.WriteLine($"::notice::SQLite load gate passed ({invariants.Count} invariants).");
            return 0;
        }

        foreach (var invariant in failed)
        {
            Console.WriteLine(
                $"::error::SQLite load gate: {invariant.Name} — {invariant.Detail}"
                    .Replace("\n", " ", StringComparison.Ordinal));
        }

        Console.WriteLine(
            $"{failed.Length} of {invariants.Count} invariants failed."
                .ToString(CultureInfo.InvariantCulture));
        return 1;
    }
}
