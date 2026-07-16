using System.Diagnostics;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The build-the-output gate: generate every build-affecting flag combination and prove it actually compiles
/// against the published Rask packages. This restores from NuGet and runs the full C# build, so it's opt-in —
/// set <c>RASK_CLI_BUILD_E2E=1</c> to run it (matches the repo's "tests run locally, not in CI" model). The
/// exhaustive file/shape assertions live in <see cref="ProjectGeneratorTests"/> and always run.
/// </summary>
public sealed class ProjectGeneratorBuildE2ETests
{
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

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            // Pin the latest published stable so restore resolves (the running test build is a prerelease).
            var version = NewCommand.ResolvePackageVersion(cliVersion: "0.0.0");
            var result = ProjectGenerator.GenerateServer(projectDir, name, auth, pwa, cqrs, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            var exit = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa},cqrs={cqrs}] generated project failed to build.");
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

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var version = NewCommand.ResolvePackageVersion(cliVersion: "0.0.0");
            var result = ProjectGenerator.GenerateWasm(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            var exit = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".csproj")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm project failed to build.");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    // wasm-hosted is a three-project solution referencing Rask.Wasm.Hosting, whose fix for the unpublishable
    // Rask.Core nuspec dep isn't in a published stable yet — so, unlike server/wasm, it can't restore from
    // NuGet. Build it against a local feed packed from THIS repo instead (also a stronger test: it exercises
    // the current package output, not a stale published one). The feed is packed once and shared across cases.
    private static readonly Lazy<Task<(string Feed, string Version)>> LocalFeed = new(PackLocalFeedAsync);

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
            // Pin the locally-packed version so restore resolves against the feed (with the fix in it).
            var result = ProjectGenerator.GenerateWasmHosted(projectDir, name, auth, pwa, docker: false, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            // Local feed first (has the fixed packages), NuGet for the framework/Microsoft.* deps.
            fs.WriteAllText(Path.Combine(projectDir, "nuget.config"),
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

            var exit = await RunDotnet($"build \"{Path.Combine(projectDir, name + ".sln")}\" -warnaserror -m:1");
            Assert.True(exit == 0, $"[auth={auth},pwa={pwa}] generated wasm-hosted solution failed to build.");
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    // Pack the three Rask packages wasm-hosted references to a temp feed and return (feedDir, packedVersion).
    private static async Task<(string Feed, string Version)> PackLocalFeedAsync()
    {
        var repoRoot = FindRepoRoot();
        var feed = Path.Combine(Path.GetTempPath(), "rask-cli-e2e-feed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(feed);

        foreach (var project in new[] { "Rask.Wasm", "Rask.Bootstrap", "Rask.Wasm.Hosting" })
        {
            var csproj = Path.Combine(repoRoot, "src", project, project + ".csproj");
            var exit = await RunDotnet($"pack \"{csproj}\" -c Release -o \"{feed}\" -m:1");
            Assert.True(exit == 0, $"failed to pack {project} for the wasm-hosted build gate.");
        }

        // Read the packed version off the nupkg filename (MinVer stamps a prerelease off the current commit).
        var nupkg = Directory.GetFiles(feed, "Rask.Wasm.Hosting.*.nupkg").Single();
        var version = Path.GetFileNameWithoutExtension(nupkg)["Rask.Wasm.Hosting.".Length..];
        return (feed, version);
    }

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

    private static async Task<int> RunDotnet(string arguments)
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

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(stdout);
            Console.Error.WriteLine(stderr);
        }

        return process.ExitCode;
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
