using System.Globalization;

namespace Rask.Benchmarks;

/// <summary>
///     Measures the two client runtimes Rask serves, and gates them against a committed baseline.
/// </summary>
/// <remarks>
///     <para>
///         Nothing measured the shipped client JS before this. <see cref="BundleSizeReport" /> walks a
///         published WASM <c>_framework/</c> and prints a table, and its own baseline README says it has
///         no committed numbers — so <c>rask.js</c> could double and every gate in the repository would
///         stay green.
///     </para>
///     <para>
///         That mattered enough to fix when the browser layer was split out of <c>rask-api.ts</c> into
///         one module per API. Splitting a file esbuild used to see as a single unit into thirty-odd is
///         exactly the change that moves a bundle — in either direction, since tree-shaking now has
///         boundaries to work with — and "we think it is about the same" is not a measurement.
///     </para>
///     <para>
///         RELEASE only, deliberately. Debug bundles are unminified, so a comment moves the number and
///         the gate would cry regression over prose. The Release bundle changes when the CODE changes,
///         which is what a size gate should track.
///     </para>
///     <para>
///         Invoke:
///         <code>
///         dotnet build -c Release src/Rask.Server &amp;&amp; dotnet build -c Release src/Rask.Wasm
///         dotnet run -c Release --project benchmarks/Rask.Benchmarks -- client-bundle-size [--check]
///         </code>
///     </para>
/// </remarks>
internal static class ClientBundleSizeReport
{
    /// <summary>Tolerated growth before the gate fails, as a fraction of the baseline.</summary>
    /// <remarks>
    ///     Not zero, unlike the payload-bytes gate. That one measures a deterministic wire encoding; this
    ///     measures esbuild's output, which can shift by a handful of bytes on a minifier version bump
    ///     that no one in this repository chose. Two percent is far below the regression worth catching
    ///     — a module accidentally pulled into the wrong bundle, or tree-shaking silently stopping — and
    ///     far above that noise.
    /// </remarks>
    private const double Tolerance = 0.02;

    private sealed record Bundle(string Name, string Path);

    private static readonly Bundle[] _bundles =
    [
        new("rask.js", Path.Combine("src", "Rask.Server", "obj", "Release", "net10.0", "rask.js")),
        new("rask.wasm.js", Path.Combine("src", "Rask.Wasm", "Browser", "rask.wasm.js")),
    ];

    public static int Run(string[] args)
    {
        var check = args.Contains("--check", StringComparer.Ordinal);
        var root = RepositoryRoot();
        if (root is null)
        {
            Console.Error.WriteLine("::error::Could not locate the repository root (no Rask.slnx above this directory).");
            return 1;
        }

        var measured = new List<(string Name, long Bytes)>();
        foreach (var bundle in _bundles)
        {
            var path = Path.Combine(root, bundle.Path);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"::error::{bundle.Name} is missing at {path}.");
                Console.Error.WriteLine(
                    "    Build both hosts in RELEASE first — a Debug build writes an unminified bundle to");
                Console.Error.WriteLine(
                    "    the same path, which would be measured as an enormous regression.");
                return 1;
            }

            measured.Add((bundle.Name, new FileInfo(path).Length));
        }

        Console.WriteLine();
        Console.WriteLine("Client runtime bundles (Release, minified):");
        foreach (var (name, bytes) in measured)
        {
            Console.WriteLine($"  {name,-16} {bytes,9:N0} bytes");
        }

        return check ? CheckAgainstBaseline(root, measured) : 0;
    }

    private static int CheckAgainstBaseline(string root, List<(string Name, long Bytes)> measured)
    {
        var baselinePath = Path.Combine(
            AppContext.BaseDirectory, "Baselines", "client-bundle-size.csv");

        if (!File.Exists(baselinePath))
        {
            Console.Error.WriteLine($"::error::Baseline not found at {baselinePath}");
            return 1;
        }

        var baseline = ParseBaseline(baselinePath);
        var regressed = false;
        var improved = false;

        Console.WriteLine();
        Console.WriteLine($"Regression check vs Baselines/client-bundle-size.csv (±{Tolerance:P0}):");

        foreach (var (name, bytes) in measured)
        {
            if (!baseline.TryGetValue(name, out var baselineBytes))
            {
                Console.Error.WriteLine(
                    $"::error::Bundle '{name}' is missing from the baseline — add it, so a new bundle "
                    + "cannot ship ungated.");
                regressed = true;
                continue;
            }

            var delta = bytes - baselineBytes;
            var budget = (long)(baselineBytes * (1 + Tolerance));
            var status = bytes > budget ? "REGRESSED"
                : delta < -(long)(baselineBytes * Tolerance) ? "improved"
                : "ok";

            Console.WriteLine(
                $"  {name,-16} {bytes,9:N0}  baseline {baselineBytes,9:N0}  {delta,+9:N0}  {status}");

            if (status == "REGRESSED")
            {
                regressed = true;
            }
            else if (status == "improved")
            {
                improved = true;
            }
        }

        // Unknown names in the baseline mean a bundle was renamed or dropped and the file was not
        // updated with it — which would leave the replacement ungated while the file looked maintained.
        foreach (var name in baseline.Keys.Where(k => measured.All(m => m.Name != k)))
        {
            Console.Error.WriteLine(
                $"::error::Baseline lists '{name}', which was not measured. Remove it, or fix the path.");
            regressed = true;
        }

        if (regressed)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "::error::A client bundle grew past its budget. That is worth understanding before it "
                + "ships: every visitor downloads this file. If the growth is intended, refresh "
                + "Baselines/client-bundle-size.csv in the same commit and say why.");
            return 1;
        }

        if (improved)
        {
            Console.WriteLine();
            Console.WriteLine(
                "A bundle shrank past the tolerance. Refresh Baselines/client-bundle-size.csv so the "
                + "file keeps tracking reality — a stale baseline stops being a gate.");
        }

        return 0;
    }

    private static Dictionary<string, long> ParseBaseline(string path)
    {
        var rows = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("bundle,", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
            {
                rows[parts[0].Trim()] = bytes;
            }
        }

        return rows;
    }

    private static string? RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir;
    }
}
