using System.Globalization;
using System.Text;

namespace Rask.Benchmarks.Sqlite;

/// <summary>Renders results as an aligned console table and as CSV.</summary>
internal static class LoadReport
{
    private const string Header =
        "Workload,Arm,Vus,Window,DurationSec,Ops,ErrOps,Throughput,p50Ms,p90Ms,p95Ms,p99Ms,p999Ms,MaxMs," +
        "BusyErrs,SqliteErrs,OtherErrs,ErrorRatio,WalBytesMax,DbBytes";

    internal static void Print(IReadOnlyList<LoadResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"{"Workload",-8} {"Arm",-24} {"VUs",4} {"ops/s",10} {"p50",8} {"p99",9} " +
                          $"{"p99.9",9} {"max",9} {"busy",6} {"err",5} {"lost",5}");
        Console.WriteLine(new string('-', 106));

        foreach (var result in results)
        {
            var s = result.Overall;
            Console.WriteLine(
                $"{result.Workload,-8} {result.Arm,-24} {result.Vus,4} {s.Throughput,10:N0} {s.P50Ms,8:N2} " +
                $"{s.P99Ms,9:N2} {s.P999Ms,9:N2} {s.MaxMs,9:N2} {s.BusyErrors,6} " +
                $"{s.SqliteErrors + s.OtherErrors,5} {result.LostWrites?.ToString(CultureInfo.InvariantCulture) ?? "-",5}");
        }

        Console.WriteLine();

        // A lost write means the harness counted a commit SQLite did not keep — that invalidates the row's
        // numbers outright, so it is shouted about rather than tucked into a column.
        foreach (var result in results.Where(r => r.LostWrites is > 0))
        {
            Console.WriteLine(
                $"!! {result.Arm} @ {result.Vus} VUs: {result.LostWrites} acknowledged commits are not in " +
                "the database. The numbers for this row are not trustworthy.");
        }

        foreach (var result in results.Where(r => r.SurplusExceedsInFlight))
        {
            Console.WriteLine(
                $"!! {result.Arm} @ {result.Vus} VUs: {result.UncountedRows} rows were never counted as " +
                $"commits, which is more than the {result.Vus} operations that can be in flight — the " +
                "harness is miscounting, not the database.");
        }

        foreach (var result in results.Where(r => r.FirstError is not null))
        {
            Console.WriteLine($"!! {result.Arm} @ {result.Vus} VUs: first error was {result.FirstError}");
        }

        // The soak's whole reason to exist: WAL growth and latency drift, neither of which a short run shows.
        foreach (var result in results.Where(r => r.Windows.Count > 1))
        {
            var first = result.Windows[0];
            var last = result.Windows[^1];
            Console.WriteLine(
                $"   {result.Arm}: WAL peaked at {result.WalBytesMax / 1024.0 / 1024.0:N1} MiB, db " +
                $"{result.DbBytes / 1024.0 / 1024.0:N1} MiB; over {result.Windows.Count} windows p50 drifted " +
                $"x{Drift(first.P50Ms, last.P50Ms):N2} and p99 x{Drift(first.P99Ms, last.P99Ms):N2} " +
                "(1.00 = flat)");
        }

        if (results.Any(r => r.PercentilesAreWindowMaxima))
        {
            Console.WriteLine(
                "Note: for runs over 60s the tail percentiles are the MAX of the per-window percentiles and " +
                "p50 is the median of window p50s — raw samples are discarded per window. Averaging " +
                "percentiles would not be arithmetic.");
        }

        Console.WriteLine(
            "Note: closed-loop harness — each VU keeps one operation in flight, so latency is service time " +
            "under N concurrent clients, not open-loop response time (a stall parks the VUs instead of " +
            "queueing work behind it).");
    }

    private static double Drift(double first, double last) => first > 0 ? last / first : 0;

    internal static string ToCsv(IReadOnlyList<LoadResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine(Header);

        foreach (var result in results)
        {
            Append(csv, result, result.Overall, window: "-");

            for (var i = 0; i < result.Windows.Count; i++)
            {
                Append(csv, result, result.Windows[i], i.ToString(CultureInfo.InvariantCulture));
            }
        }

        return csv.ToString();
    }

    private static void Append(StringBuilder csv, LoadResult result, LatencyStats s, string window)
    {
        var c = CultureInfo.InvariantCulture;
        csv.AppendLine(string.Join(',', [
            result.Workload,
            result.Arm,
            result.Vus.ToString(c),
            window,
            s.DurationSeconds.ToString("F2", c),
            s.Ops.ToString(c),
            s.ErrorOps.ToString(c),
            s.Throughput.ToString("F1", c),
            s.P50Ms.ToString("F3", c),
            s.P90Ms.ToString("F3", c),
            s.P95Ms.ToString("F3", c),
            s.P99Ms.ToString("F3", c),
            s.P999Ms.ToString("F3", c),
            s.MaxMs.ToString("F3", c),
            s.BusyErrors.ToString(c),
            s.SqliteErrors.ToString(c),
            s.OtherErrors.ToString(c),
            s.ErrorRatio.ToString("F5", c),
            result.WalBytesMax.ToString(c),
            result.DbBytes.ToString(c),
        ]));
    }
}
