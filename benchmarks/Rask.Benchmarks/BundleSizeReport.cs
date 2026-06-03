using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Benchmarks;

// One-shot measurement utility (NOT a BenchmarkDotNet benchmark) that walks a published
// WASM AppBundle's _framework/ directory and prints a per-asset size table. Reports raw
// bytes plus brotli/gzip siblings when present, so PR5 (minify rask.wasm.js) and PR6
// (pre-compressed asset serving) can both cite hard numbers.
//
// Invoke:
//   dotnet publish -c Release Rask.Example.Wasm.Host
//   dotnet run -c Release --project Rask.Benchmarks -- bundle-size [path-to-_framework]
//
// If no path is given, looks in the standard publish locations.
internal static class BundleSizeReport
{
    // Mirrors Rask.Wasm.Hosting.RaskWasmEndpointExtensions.IsFingerprintedAsset — kept
    // duplicated rather than pulled in via InternalsVisibleTo to keep the report's
    // dependency surface small (no Rask.Wasm.Hosting reference).
    private static readonly Regex _fingerprintRegex = new(
        @"\.[0-9a-z]{10,}\.[^.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static int Run(string[] args)
    {
        var frameworkDir = args.Length > 1
            ? args[1]
            : FindDefaultFrameworkDir();

        if (frameworkDir is null || !Directory.Exists(frameworkDir))
        {
            Console.Error.WriteLine(
                "Could not locate a _framework/ directory. Pass a path explicitly:");
            Console.Error.WriteLine(
                "  dotnet run -c Release --project Rask.Benchmarks -- bundle-size <path-to-_framework>");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Run `dotnet publish -c Release Rask.Example.Wasm.Host` first to generate the bundle.");
            return 1;
        }

        var files = Directory.EnumerateFiles(frameworkDir)
            .Select(f => new FileInfo(f))
            .ToList();

        // Group by stem so .wasm + .wasm.br + .wasm.gz line up on one row.
        var rows = files
            .Where(f => !f.Name.EndsWith(".br", StringComparison.Ordinal)
                        && !f.Name.EndsWith(".gz", StringComparison.Ordinal))
            .Select(f => new Row(
                f.Name,
                f.Length,
                TryLength(Path.Combine(frameworkDir, f.Name + ".br")),
                TryLength(Path.Combine(frameworkDir, f.Name + ".gz"))))
            .OrderByDescending(r => r.Raw)
            .ToList();

        Console.WriteLine($"Framework directory: {frameworkDir}");
        Console.WriteLine();
        Console.WriteLine($"{"File",-50} {"Raw",12} {"Br",12} {"Gz",12} {"Cache",-10}");
        Console.WriteLine(new string('-', 100));

        long totalRaw = 0, totalBr = 0, totalGz = 0;
        var immutableCount = 0;
        foreach (var r in rows)
        {
            var cache = IsFingerprinted(r.Name) ? "immutable" : "no-cache";
            if (cache == "immutable")
            {
                immutableCount++;
            }

            Console.WriteLine($"{Trim(r.Name, 50),-50} {Fmt(r.Raw),12} {Fmt(r.Br),12} {Fmt(r.Gz),12} {cache,-10}");
            totalRaw += r.Raw;
            totalBr += r.Br ?? r.Raw;
            totalGz += r.Gz ?? r.Raw;
        }

        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"TOTAL",-50} {Fmt(totalRaw),12} {Fmt(totalBr),12} {Fmt(totalGz),12}");
        Console.WriteLine();
        Console.WriteLine($"  {rows.Count} files ({immutableCount} immutable, {rows.Count - immutableCount} no-cache)");
        Console.WriteLine($"  Raw total: {Fmt(totalRaw)} ({totalRaw:N0} bytes)");
        Console.WriteLine($"  Br  total: {Fmt(totalBr)} ({totalBr:N0} bytes) — {Percent(totalBr, totalRaw)} of raw");
        Console.WriteLine($"  Gz  total: {Fmt(totalGz)} ({totalGz:N0} bytes) — {Percent(totalGz, totalRaw)} of raw");

        // Highlight client-script footprint for PR5.
        var clientScripts = rows.Where(r =>
            r.Name.Equals("rask.wasm.js", StringComparison.Ordinal)
            || r.Name.Equals("rask.js", StringComparison.Ordinal)
            || r.Name.Equals("rask-morph.js", StringComparison.Ordinal)).ToList();
        if (clientScripts.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Rask client scripts (PR5 target):");
            foreach (var c in clientScripts)
            {
                Console.WriteLine($"  {c.Name,-20} raw {Fmt(c.Raw),8}  br {Fmt(c.Br),8}  gz {Fmt(c.Gz),8}");
            }
        }

        return 0;
    }

    private static long? TryLength(string path)
        => File.Exists(path) ? new FileInfo(path).Length : null;

    private static string Fmt(long? bytes)
    {
        if (bytes is null)
        {
            return "-";
        }

        var n = bytes.Value;
        return n switch
        {
            < 1024 => $"{n} B",
            < 1024 * 1024 => $"{n / 1024.0:F1} KB",
            _ => $"{n / (1024.0 * 1024.0):F2} MB"
        };
    }

    private static string Percent(long part, long whole)
        => whole == 0 ? "-" : ((double)part / whole).ToString("P1", CultureInfo.InvariantCulture);

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string? FindDefaultFrameworkDir()
    {
        // Walk up from the running assembly's location to find the repo root, then check
        // the standard publish output locations.
        var root = FindRepoRoot();
        if (root is null)
        {
            return null;
        }

        string[] candidates =
        [
            Path.Combine(root, "samples", "Rask.Example.Wasm.Host", "bin", "Release", "net10.0", "publish", "wwwroot",
                "_framework"),
            Path.Combine(root, "samples", "Rask.Example.Wasm", "bin", "Release", "net10.0-browser", "publish", "wwwroot",
                "_framework"),
            Path.Combine(root, "samples", "Rask.Example.Wasm", "bin", "Release", "net10.0-browser", "browser-wasm", "publish",
                "wwwroot", "_framework")
        ];

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.GetFiles(dir.FullName, "Rask.slnx").Length > 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsFingerprinted(string fileName) => _fingerprintRegex.IsMatch(fileName);

    private readonly record struct Row(string Name, long Raw, long? Br, long? Gz);
}
