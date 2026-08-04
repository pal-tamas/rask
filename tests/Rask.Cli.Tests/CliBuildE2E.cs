using System.Diagnostics;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The shared machinery behind the CLI build-the-output gates: pack <b>this commit's</b> Rask packages to a
/// local feed once, drop a generated project's files on disk, point it at that feed, and run the real
/// <c>dotnet build</c>. Both <see cref="ProjectGeneratorBuildE2ETests"/> (every scaffold flag combination) and
/// <see cref="TutorialWalkthroughE2ETests"/> (the docs walk-through) consume it, so the nine-project pack runs
/// once per test session rather than once per file.
/// </summary>
/// <remarks>
/// Every case builds against the local feed rather than the latest published stable. That's the faithful
/// contract — the CLI and the packages are released together under one tag, so a generated project pins the
/// version of the CLI that made it — and it's what lets a gate catch a break in the same commit that
/// introduces it instead of one release later.
/// </remarks>
internal static class CliBuildE2E
{
    /// <summary>
    /// Every packable Rask package a generated project, feature, job, email, or later-chapter pillar can
    /// reference, packed once into a shared feed. <c>Rask.Core</c> is deliberately absent — it is
    /// <c>IsPackable=false</c> and ships bundled inside <c>Rask.Server</c>/<c>Rask.Wasm</c>'s <c>lib/</c>, so
    /// packing it would produce nothing to restore.
    /// </summary>
    internal static readonly string[] FeedPackages =
    [
        "Rask.Server",                      // server template
        "Rask.Wasm",                        // wasm + wasm-hosted templates
        "Rask.Wasm.Hosting",                // wasm-hosted template
        "Rask.Bootstrap",                   // every template, and `generate feature --bs`
        "Rask.Cqrs",                        // server template --cqrs, and every generated feature
        "Rask.Data",                        // every generated feature
        "Rask.SQLite",                      // --data + every generated feature (via Rask.SQLite.EntityFrameworkCore)
        "Rask.SQLite.EntityFrameworkCore",  // server template --data and generated features that own a context (UseRaskSqlite)
        "Rask.SQLite.Litestream",           // --litestream — AddRaskSqliteLitestream / RestoreSqliteFromLitestreamAsync
        "Rask.SQLite.Snapshots",            // --snapshots — AddRaskSqliteSnapshots
        "Rask.WebPush",                     // --push — AddRaskWebPush + the subscription endpoints
        "Rask.Outbox",                      // generate feature --outbox, and tutorial ch7
        "Rask.Jobs",                        // generate job, and tutorial ch4
        "Rask.Mail",                        // generate email, and tutorial ch5
        "Rask.Cache",                       // tutorial ch6 — AddRaskCache / ICache.GetOrCreateAsync
        "Rask.Validation.DataAnnotations",  // generate feature --validation dataannotations
        "Rask.Validation.FluentValidation", // generate feature --validation fluent
    ];

    // Packed once and shared across every case (packing the projects is the expensive part of these gates).
    internal static readonly Lazy<Task<(string Feed, string Version)>> LocalFeed = new(PackLocalFeedAsync);

    /// <summary>
    /// Why a build gate didn't run. Reported through <c>Skip.IfNot</c> so an un-run gate shows up as SKIPPED in
    /// the test output instead of passing silently — these are the only tests that prove the CLI emits code that
    /// actually compiles, so "green" must never be able to mean "never ran".
    /// </summary>
    internal const string SkipReason =
        "CLI build gate: set RASK_CLI_BUILD_E2E=1 to run it (it packs this commit's Rask packages, restores, " +
        "and builds the generated projects, so it needs the SDK and network). See scripts/run-cli-build-e2e.sh.";

    /// <summary>True when the build-the-output gates are opted into (they restore + build, needing the SDK and network).</summary>
    internal static bool Enabled => Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") == "1";

    /// <summary>Packs <see cref="FeedPackages"/> to a temp feed; returns its directory and the packed version.</summary>
    private static async Task<(string Feed, string Version)> PackLocalFeedAsync()
    {
        var repoRoot = FindRepoRoot();
        var feed = Path.Combine(Path.GetTempPath(), "rask-cli-e2e-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feed);

        foreach (var project in FeedPackages)
        {
            var csproj = Path.Combine(repoRoot, "src", project, project + ".csproj");
            var (exit, output) = await RunDotnet($"pack \"{csproj}\" -c Release -o \"{feed}\" -m:1");
            Assert.True(exit == 0, $"failed to pack {project} for the build gate.{Diagnostics(output)}");
        }

        // Read the packed version off a nupkg filename (MinVer stamps a prerelease off the current commit).
        // Every project packs at the same version, so any one of them answers for the set — Rask.Server is
        // used because no other package's id starts with it (Rask.Wasm.* would match two).
        var nupkg = Directory.GetFiles(feed, "Rask.Server.*.nupkg").Single();
        var version = Path.GetFileNameWithoutExtension(nupkg)["Rask.Server.".Length..];
        return (feed, version);
    }

    // The packages a generated project needs, written straight into the csproj. Rask.* come from the local
    // feed at the packed version; everything else takes the version this repo pins, so the gate can't drift
    // from the rest of the build.
    internal static void InjectPackages(SystemFileSystem fs, string csproj, IReadOnlyList<string> packages, string raskVersion)
    {
        var pins = RepoPackagePins();
        var refs = string.Join(
            "\n",
            packages.Select(p => $"""    <PackageReference Include="{p}" Version="{VersionFor(p, pins, raskVersion)}"/>"""));

        var content = fs.ReadAllText(csproj);
        fs.WriteAllText(csproj, content.Replace("</Project>", $"  <ItemGroup>\n{refs}\n  </ItemGroup>\n\n</Project>", StringComparison.Ordinal));
    }

    private static string VersionFor(string package, IReadOnlyDictionary<string, string> pins, string raskVersion)
    {
        if (package.StartsWith("Rask.", StringComparison.Ordinal))
        {
            return raskVersion;
        }

        if (pins.TryGetValue(package, out var pinned))
        {
            return pinned;
        }

        // EF's Design package isn't referenced by this repo, so it has no pin of its own — but it ships in
        // lockstep with EF Core, so borrow that version rather than inventing one.
        if (package.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
        {
            return pins["Microsoft.EntityFrameworkCore"];
        }

        throw new InvalidOperationException($"No version known for '{package}'. Pin it in Directory.Packages.props.");
    }

    private static Dictionary<string, string> RepoPackagePins()
    {
        var props = File.ReadAllText(Path.Combine(FindRepoRoot(), "Directory.Packages.props"));
        return Regex.Matches(props, """<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"\s*/>""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
    }

    // Local feed first (this commit's packages), nuget.org for the framework/Microsoft.* deps.
    internal static void WriteNuGetConfig(SystemFileSystem fs, string projectDir, string feed) =>
        fs.WriteAllText(
            Path.Combine(projectDir, "nuget.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear/>
                <add key="local" value="{feed}"/>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>
              </packageSources>
            </configuration>
            """);

    internal static string FindRepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }

    internal static async Task<(int Exit, string Output)> RunDotnet(string arguments)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["CI"] = "true";

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }

    /// <summary>
    /// The child process's diagnostics, folded into the assertion message. xUnit reports the message but not
    /// the child's console, so without this a failure says only *that* the build broke — never why.
    /// </summary>
    internal static string Diagnostics(string output)
    {
        var errors = output
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Contains(": error ", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(15)
            .ToArray();

        return "\n" + string.Join("\n", errors.Length > 0 ? errors : output.Split('\n').TakeLast(20));
    }

    internal static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }
}
