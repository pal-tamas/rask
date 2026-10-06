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
    /// reference, packed once into a shared feed. <c>Rask.Core</c> and <c>Rask.Tailwind</c> are
    /// deliberately absent — both are <c>IsPackable=false</c> and ship bundled inside
    /// <c>Rask.Server</c>/<c>Rask.Wasm</c> (Core in <c>lib/</c>, Tailwind in <c>build/</c>), so packing
    /// them would produce nothing to restore. <c>Rask.Bootstrap</c> is absent because it no longer
    /// exists; it outlived the project by one list, and this gate is opt-in, so nothing said so.
    /// </summary>
    internal static readonly string[] FeedPackages =
    [
        "Rask",                             // the shared core every host depends on — the chain, routing, forms, the generators
        "Rask.Postgres",                    // Rask.Server depends on it — the provider Rask:Database:Provider=postgres picks
        "Rask.SqlServer",                   // and sqlserver
        "Rask.Server",                      // server template: the host and every battery, RaskApp included
        "Rask.Wasm",                        // the wasm template, and wasm-hosted's browser half
        "Rask.Web",                         // Rask.Server and Rask.Wasm depend on it: MDN's web APIs, its WASM-only ones extending them
        "Rask.Cqrs",                        // server template --cqrs, and every generated feature
        "Rask.Wire",                        // Rask.Cqrs depends on it: the wire primitives its codecs call
        "Rask.Api",                         // API hosting + the client generator (server half)
        "Rask.Api.Client",                  // the runtime the generated client calls, on both halves
        "Rask.Query",                       // wired by default wherever --cqrs is
        "Rask.Cqrs.Client",                 // wasm-hosted: the browser half of remote dispatch
        "Rask.Cqrs.Server",                 // wasm-hosted: the endpoint half
        "Rask.Spa.Hosting",                 // Rask.Server depends on it: MapRaskSpa serves wasm-hosted's client
        "Rask.Auth",                        // --data: the scaffolded context maps the account tables
        "Rask.Auth.Client",                 // Rask's browser half depends on it
        "Rask.Auth.Api",                    // Rask.Auth depends on it
        "Rask.Data",                        // every generated feature
        "Rask.SQLite",                      // --data + every generated feature (via Rask.SQLite.EntityFrameworkCore)
        "Rask.SQLite.EntityFrameworkCore",  // server template --data and generated features that own a context (UseRaskSqlite)
        "Rask.SQLite.Litestream",           // --data — continuous backup on the golden path
        "Rask.SQLite.Snapshots",            // --snapshots — AddRaskSqliteSnapshots
        "Rask.WebPush",                     // --push — AddRaskWebPush + the subscription endpoints
        "Rask.Outbox",                      // tutorial ch.7
        "Rask.Jobs",                        // generate job, and tutorial ch4
        "Rask.Mail",                        // generate email, and tutorial ch5
        "Rask.Cache",                       // tutorial ch6 — AddRaskCache / Cache.Remember
        "Rask.Storage",                     // on by default — AddRaskStorage / MapRaskStorage
        "Rask.Logging",                     // --logs — AddRaskLogging, and the dashboard's History mode
        "Rask.Dashboard",                   // --ops — AddRaskDashboard + the /_rask pages
        "Rask.Ui",                          // the component kit Rask.Dashboard is drawn with, and depends on
        "Rask.DevTools",                    // every template — the in-page devtools of a Debug build
        "Rask.Validation.FluentValidation", // the FluentValidation alternative
        "Rask.External",                    // --islands: the island base classes and the build layer
        "Rask.Blazor",                      // --islands blazor: a Razor component as a Rask component
        "Rask.Testing",                     // server + wasm: the <name>.Tests project's one passing test
    ];

    // Packed once and shared across every case (packing the projects is the expensive part of these gates).
    internal static readonly Lazy<Task<(string Feed, string Version)>> LocalFeed = new(PackLocalFeedAsync);

    /// <summary>
    /// Why a build gate didn't run. Reported through <c>Assert.SkipUnless</c> so an un-run gate shows up as SKIPPED in
    /// the test output instead of passing silently — these are the only tests that prove the CLI emits code that
    /// actually compiles, so "green" must never be able to mean "never ran".
    /// </summary>
    internal const string SkipReason =
        "CLI build gate: set RASK_CLI_BUILD_E2E=1 to run it (it packs this commit's Rask packages, restores, " +
        "and builds the generated projects, so it needs the SDK and network). See scripts/run-cli-build-e2e.sh.";

    /// <summary>True when the build-the-output gates are opted into (they restore + build, needing the SDK and network).</summary>
    internal static bool Enabled => Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") == "1";

    /// <summary>
    ///     The project that publishes <paramref name="packageId"/>: <c>src/&lt;id&gt;/&lt;id&gt;.csproj</c> for every
    ///     package but one — <c>Rask</c>, the shared core, is <c>src/Rask.Core</c> under its own PackageId.
    /// </summary>
    internal static string ProjectFor(string repoRoot, string packageId)
    {
        var byName = Path.Combine(repoRoot, "src", packageId, packageId + ".csproj");
        if (File.Exists(byName))
        {
            return byName;
        }

        var declared = Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(path => File.ReadAllText(path).Contains($"<PackageId>{packageId}</PackageId>", StringComparison.Ordinal));

        return declared ?? throw new FileNotFoundException($"No project under src/ publishes the package '{packageId}'.");
    }

    /// <summary>Packs <see cref="FeedPackages"/> to a temp feed; returns its directory and the packed version.</summary>
    private static async Task<(string Feed, string Version)> PackLocalFeedAsync()
    {
        var repoRoot = FindRepoRoot();
        var feed = Path.Combine(Path.GetTempPath(), "rask-cli-e2e-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feed);

        // Built once, packed one at a time. The build is the expensive half and safe to do side by side
        // in ONE MSBuild invocation, where a project several packages share is compiled once. The pack is
        // still a `dotnet pack` per package: packing them all from one invocation named a referenced
        // project at version 1.0.0 instead of the MinVer version it was packed at, and no scaffold could
        // restore. With --no-build each of those is a few seconds of writing a nuspec.
        var (built, buildOutput) = await RunDotnet(
            $"msbuild \"{WriteBuildTraversal(repoRoot)}\" -t:Build -m:{BuildSlots} -nologo -v:minimal");
        Assert.True(built == 0, $"failed to build the feed's projects for the build gate.{Diagnostics(buildOutput)}");

        foreach (var package in FeedPackages)
        {
            var csproj = ProjectFor(repoRoot, package);
            var (exit, output) = await RunDotnet($"pack \"{csproj}\" -c Release -o \"{feed}\" -m:1 --no-build");
            Assert.True(exit == 0, $"failed to pack {package} for the build gate.{Diagnostics(output)}");
        }

        // Read the packed version off a nupkg filename (MinVer stamps a prerelease off the current commit).
        // Every project packs at the same version, so any one of them answers for the set — Rask.Server is
        // used because no other package's id starts with it (Rask.Wasm.* would match two).
        var nupkg = Directory.GetFiles(feed, "Rask.Server.*.nupkg").Single();
        var version = Path.GetFileNameWithoutExtension(nupkg)["Rask.Server.".Length..];

        AssertTheCoreShipsFromItsOwnPackageOnly(feed, nupkg);
        AssertNoPackageShipsBuildIntermediates(feed);

        EvictFromGlobalCache(version);
        return (feed, version);
    }

    /// <summary>How many cores the feed's build may take: one on a shared machine, the runner's own in CI.</summary>
    private static string BuildSlots =>
        Environment.GetEnvironmentVariable("RASK_BUILD_SLOTS") is { Length: > 0 } slots ? slots : "1";

    /// <summary>A project that restores and builds every one of <see cref="FeedPackages"/> in Release.</summary>
    /// <remarks>
    ///     The restore carries a session id of its own so the build does not reuse the evaluation made
    ///     before the NuGet imports existed — the reason scripts/run-unit-local.sh gives for the same shape.
    /// </remarks>
    private static string WriteBuildTraversal(string repoRoot)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rask-cli-e2e-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var projects = string.Join(
            Environment.NewLine,
            FeedPackages.Select(package => $"    <FeedProject Include=\"{ProjectFor(repoRoot, package)}\" />"));
        var traversal = Path.Combine(directory, "build.proj");
        File.WriteAllText(traversal, $"""
            <Project>
              <ItemGroup>
            {projects}
              </ItemGroup>
              <Target Name="Build">
                <MSBuild Projects="@(FeedProject)" Targets="Restore" Properties="MSBuildRestoreSessionId=$([System.Guid]::NewGuid())" />
                <MSBuild Projects="@(FeedProject)" Targets="Build" Properties="Configuration=Release" BuildInParallel="true" />
              </Target>
            </Project>
            """);
        return traversal;
    }

    /// <summary>No package carries a file out of a project's <c>obj/</c> as content.</summary>
    /// <remarks>
    ///     NuGet packs every <c>Content</c> item, and Rask.Tailwind declared its compiled sheet as one whenever the file
    ///     did not exist yet — which is exactly a clean clone. A package packed there shipped
    ///     <c>content/obj/…/ui.generated.css</c>, and a consumer's project picked a build intermediate up as its own
    ///     content (#1090). This gate packs from a fresh feed, so it is the place that sees the clean-clone shape.
    /// </remarks>
    private static void AssertNoPackageShipsBuildIntermediates(string feed)
    {
        var offenders = new List<string>();
        foreach (var package in Directory.GetFiles(feed, "*.nupkg"))
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(package);
            offenders.AddRange(zip.Entries
                .Select(e => e.FullName)
                .Where(name => name.StartsWith("content/obj/", StringComparison.OrdinalIgnoreCase)
                               || (name.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase)
                                   && name.Contains("/obj/", StringComparison.OrdinalIgnoreCase)))
                .Select(name => $"{Path.GetFileName(package)}: {name}"));
        }

        Assert.True(
            offenders.Count == 0,
            "These packages ship build intermediates from obj/ as content, which every consumer then treats as its own "
            + "content:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Rask.Core ships in the <c>Rask</c> package and in no host: one copy per process.</summary>
    private static void AssertTheCoreShipsFromItsOwnPackageOnly(string feed, string serverPackage)
    {
        // The core is the `Rask` package: `Rask.<version>.nupkg`, the one whose name has a digit right after the id.
        var core = Directory.GetFiles(feed, "Rask.*.nupkg")
            .Single(path => char.IsDigit(Path.GetFileName(path)["Rask.".Length]));
        using var coreZip = System.IO.Compression.ZipFile.OpenRead(core);
        Assert.Contains(coreZip.Entries, e => e.FullName == "lib/net10.0/Rask.Core.dll");

        // And the hosts no longer bundle it: one copy per process, from one package.
        using var server = System.IO.Compression.ZipFile.OpenRead(serverPackage);
        Assert.DoesNotContain(server.Entries, e => e.FullName.EndsWith("/Rask.Core.dll", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Drops this version of the Rask packages from the NuGet global cache, so the restore below has to
    ///     take the ones just packed.
    /// </summary>
    /// <remarks>
    ///     MinVer stamps a version from the commit and its height, so every pack of an un-committed working
    ///     tree produces the <em>same</em> version string with different content. NuGet keys its cache on
    ///     id+version alone: once <c>Rask.Server 0.19.1-alpha.0.31</c> is extracted there, every later
    ///     restore reuses it and silently ignores the freshly packed nupkg in the local feed — so the gate
    ///     builds against whatever the first pack of that version happened to contain, and a change made
    ///     afterwards is never actually tested. That is a green gate over stale bits, which is worse than no
    ///     gate. Evicting is safe: these are prerelease packages this repo just built, and they are always
    ///     re-restorable from the feed.
    /// </remarks>
    private static void EvictFromGlobalCache(string version)
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                   ?? Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        foreach (var package in FeedPackages)
        {
            // The cache lowercases both segments.
            var dir = Path.Combine(root, package.ToLowerInvariant(), version.ToLowerInvariant());
            TryDeleteDirectory(dir);
        }
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

    // Parsed as XML by RepoPins rather than by a regex here: the regex this replaced only matched the
    // exact self-closing single-space form, so reformatting Directory.Packages.props would have dropped
    // packages silently and surfaced as VersionFor's "No version known" throw.
    private static Dictionary<string, string> RepoPackagePins() => RepoPins.Packages();

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

    /// <summary>Runs any executable, for the scaffolders and bundlers that are not <c>dotnet</c>.</summary>
    /// <remarks>
    ///     Arguments are passed through <c>ArgumentList</c> rather than joined into a string, so a path with
    ///     a space in it (a temp directory on some machines) does not silently split into two arguments.
    /// </remarks>
    internal static async Task<(int Exit, string Output)> RunProcess(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var result = await TestProcess.Run(
            fileName, arguments, workingDirectory, new Dictionary<string, string?> { ["CI"] = "true" }, LongStepTimeout);
        return (result.ExitCode, result.Output);
    }

    // A nine-package pack or a scaffold's first restore+build runs for minutes on a loaded machine; the ceiling is
    // there to turn a hang into a red, not to race a slow but working build.
    internal static readonly TimeSpan LongStepTimeout = TimeSpan.FromMinutes(30);

    internal static async Task<(int Exit, string Output)> RunDotnet(string arguments)
    {
        // Node reuse is what makes the wasm-hosted cases fail intermittently, and only ever on a repeated
        // run (#650). A worker node kept alive from an earlier run has already loaded Rask.Wasm.Tasks.dll
        // via Assembly.LoadFrom from *that* run's temp directory; the next run's publish reuses the node,
        // the load of the same simple name from a new path throws, and the scoped-asset bake silently
        // produces nothing — surfacing as "the bake did not run during this publish" rather than as
        // anything to do with the template. The repo's Directory.Build.rsp sets this for in-repo builds,
        // but these projects are generated into a temp directory outside it, so it has to be set here.
        // An environment variable rather than -nodeReuse:false on the command line: the wasm-hosted build
        // shells out to a nested `dotnet publish`, and the variable is inherited where a flag is not.
        var result = await TestProcess.Run(
            "dotnet",
            arguments,
            environment: new Dictionary<string, string?> { ["CI"] = "true", ["MSBUILDDISABLENODEREUSE"] = "1" },
            timeout: LongStepTimeout);
        return (result.ExitCode, result.Output);
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
