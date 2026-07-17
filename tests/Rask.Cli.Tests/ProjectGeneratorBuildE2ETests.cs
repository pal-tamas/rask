using System.Diagnostics;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The build-the-output gate: generate every build-affecting flag combination and prove it actually compiles
/// against <b>this commit's</b> Rask packages, packed to a local feed. This packs the repo, restores, and runs
/// the full C# build, so it's opt-in — set <c>RASK_CLI_BUILD_E2E=1</c> to run it (matches the repo's "tests run
/// locally, not in CI" model). The exhaustive file/shape assertions live in <see cref="ProjectGeneratorTests"/>
/// and always run.
/// </summary>
/// <remarks>
/// Every case builds against the local feed rather than the latest published stable. That's the faithful
/// contract — the CLI and the packages are released together under one tag, so a generated project pins the
/// version of the CLI that made it — and it's what lets the gate catch a break in the same commit that
/// introduces it instead of one release later.
/// </remarks>
public sealed class ProjectGeneratorBuildE2ETests
{
    /// <summary>
    /// Every packable Rask package a generated project or feature can reference, packed once into a shared
    /// feed. <c>Rask.Core</c> is deliberately absent — it is <c>IsPackable=false</c> and ships bundled inside
    /// <c>Rask.Server</c>/<c>Rask.Wasm</c>'s <c>lib/</c>, so packing it would produce nothing to restore.
    /// </summary>
    private static readonly string[] FeedPackages =
    [
        "Rask.Server",                      // server template
        "Rask.Wasm",                        // wasm + wasm-hosted templates
        "Rask.Wasm.Hosting",                // wasm-hosted template
        "Rask.Bootstrap",                   // every template, and `generate feature --bs`
        "Rask.Cqrs",                        // server template --cqrs, and every generated feature
        "Rask.Data",                        // every generated feature
        "Rask.Outbox",                      // generate feature --outbox
        "Rask.Validation.DataAnnotations",  // generate feature --validation dataannotations
        "Rask.Validation.FluentValidation", // generate feature --validation fluent
    ];

    // Packed once and shared across every case (packing nine projects is the expensive part of this gate).
    private static readonly Lazy<Task<(string Feed, string Version)>> LocalFeed = new(PackLocalFeedAsync);
    // docker doesn't affect the build (just adds Dockerfile/.dockerignore), so the 3 build-relevant flags
    // give 2³ = 8 combinations — every scenario, per the "test every scenario" directive.
    public static IEnumerable<object[]> BuildAffectingCombinations()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0];
        }
    }

    [Theory]
    [MemberData(nameof(BuildAffectingCombinations))]
    public async Task Generated_server_project_builds(bool auth, bool pwa, bool cqrs)
    {
        if (Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") != "1")
        {
            return; // opt-in: this restores + builds, needing the SDK and network.
        }

        var name = $"E2E{(auth ? "A" : "")}{(pwa ? "P" : "")}{(cqrs ? "Q" : "")}";
        if (name == "E2E")
        {
            name = "E2ENone";
        }

        var (feed, version) = await LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(projectDir, name, auth, pwa, cqrs, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa},cqrs={cqrs}] generated project failed to build.{Diagnostics(output)}");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    // wasm build-affecting flags are auth/pwa (docker only adds files) → 2² = 4 combinations.
    public static IEnumerable<object[]> WasmBuildAffectingCombinations()
    {
        for (var mask = 0; mask < 4; mask++)
        {
            yield return [(mask & 1) != 0, (mask & 2) != 0];
        }
    }

    [Theory]
    [MemberData(nameof(WasmBuildAffectingCombinations))]
    public async Task Generated_wasm_project_builds(bool auth, bool pwa)
    {
        if (Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") != "1")
        {
            return; // opt-in: this restores + builds, needing the SDK and network.
        }

        var name = $"WE2E{(auth ? "A" : "")}{(pwa ? "P" : "")}";
        if (name == "WE2E")
        {
            name = "WE2ENone";
        }

        var (feed, version) = await LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateWasm(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm project failed to build.{Diagnostics(output)}");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    [Theory]
    [MemberData(nameof(WasmBuildAffectingCombinations))]
    public async Task Generated_wasm_hosted_solution_builds(bool auth, bool pwa)
    {
        if (Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") != "1")
        {
            return; // opt-in: this packs the repo + restores + builds the WASM solution.
        }

        var (feed, version) = await LocalFeed.Value;

        var name = $"HE2E{(auth ? "A" : "")}{(pwa ? "P" : "")}";
        if (name == "HE2E")
        {
            name = "HE2ENone";
        }

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateWasmHosted(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".sln")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm-hosted solution failed to build.{Diagnostics(output)}");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    /// <summary>
    /// A feature run that names relationship targets emits several entities at once — each in its own folder
    /// and namespace, all sharing one DbContext that lives with the root. That shape is the first generated
    /// code to carry cross-namespace <c>using</c>s, which string assertions can't validate: a target's handlers
    /// name a DbContext declared in the root's namespace, and the DbContext names types in each target's.
    /// Only a real compile proves those resolve.
    /// </summary>
    [Fact]
    public async Task Generated_multi_entity_feature_builds()
    {
        if (Environment.GetEnvironmentVariable("RASK_CLI_BUILD_E2E") != "1")
        {
            return; // opt-in: this packs the repo + restores + builds.
        }

        var (feed, version) = await LocalFeed.Value;

        const string Name = "FE2E";
        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, Name);
        try
        {
            var fs = new SystemFileSystem();

            var host = ProjectGenerator.GenerateServer(projectDir, Name, auth: false, pwa: false, cqrs: false, docker: false, version);
            foreach (var file in host.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            var post = new EntitySpec("Post", "Posts", [new FieldSpec("Title", "string", IsNullable: false, MaxLength: 200)]);
            var comment = new EntitySpec("Comment", "Comments", [new FieldSpec("Body", "string", IsNullable: false, MaxLength: 200)]);
            var feature = FeatureGenerator.Generate(
                new ProjectContext(projectDir, Name),
                projectDir,
                new FeatureSpec(post, [new RelationshipSpec(Cardinality.OneToMany, IsOptional: false, post, comment)]),
                new FeatureOptions { IdType = "Guid", Validation = "valueobjects" });

            foreach (var file in feature.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // `dotnet add package` is GenerateCommand's job, not the generator's — so add what the generator
            // says it needs. Driving off result.Packages keeps this from drifting from what the CLI does.
            InjectPackages(fs, Path.Combine(projectDir, Name + ".csproj"), feature.Packages, version);
            WriteNuGetConfig(fs, projectDir, feed);

            var (exit, output) = await RunDotnet($"build \"{Path.Combine(projectDir, Name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"generated multi-entity feature failed to build.{Diagnostics(output)}");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    // The packages a generated feature needs, written straight into the csproj. Rask.* come from the local
    // feed at the packed version; everything else takes the version this repo pins, so the gate can't drift
    // from the rest of the build.
    private static void InjectPackages(SystemFileSystem fs, string csproj, IReadOnlyList<string> packages, string raskVersion)
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

    // Local feed first (this commit's packages), nuget.org for the framework/Microsoft.* deps.
    private static void WriteNuGetConfig(SystemFileSystem fs, string projectDir, string feed) =>
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

    private static string FindRepoRoot()
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

    private static async Task<(int Exit, string Output)> RunDotnet(string arguments)
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
    private static string Diagnostics(string output)
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

    private static void TryDeleteDirectory(string path)
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
