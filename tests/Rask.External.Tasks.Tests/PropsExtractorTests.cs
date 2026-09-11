using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Utilities;
using Rask.TypeScript.Tasks;

namespace Rask.External.Tasks.Tests;

/// <summary>
///     Runs the bundled props extractor under node, with the Rask-pinned compiler, against fixture packages, and
///     holds what it writes to the committed expected snapshots byte for byte.
/// </summary>
/// <remarks>
///     <para>
///         Byte for byte because the snapshot is committed: any difference in key order, number formatting or
///         line endings is a diff in someone's review, so it is part of the contract rather than formatting.
///     </para>
///     <para>
///         Skipped, by name, where node is not installed or the pinned compiler can neither be found in the cache
///         nor fetched. Set <c>RASK_UPDATE_EXTRACTOR_SNAPSHOTS=1</c> to rewrite the expected files after a
///         deliberate change, and review that diff like any other snapshot change.
///     </para>
/// </remarks>
public sealed class PropsExtractorTests : IDisposable
{
    private static readonly string Fixtures =
        Path.Combine(RepoRoot(), "tests", "Rask.External.Tasks.Tests", "ExtractorFixtures");

    private readonly string _project = Directory.CreateTempSubdirectory("rask-extract").FullName;

    public void Dispose() => Directory.Delete(_project, recursive: true);

    [SkippableFact]
    public void Each_island_is_written_exactly_as_its_committed_snapshot()
    {
        var typescript = Toolchain();
        var output = Extract(
            typescript,
            Island("FixtureButton", "fixture-button"),
            Island("Badge", "fixture-button#Badge"),
            Island("Switch", "fixture-button#Switch"));

        foreach (var name in new[] { "Badge", "FixtureButton", "Switch" })
        {
            var actual = File.ReadAllText(Path.Combine(output, name + ".props.json"));
            var expected = Path.Combine(Fixtures, "expected", name + ".props.json");

            if (Environment.GetEnvironmentVariable("RASK_UPDATE_EXTRACTOR_SNAPSHOTS") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
                File.WriteAllText(expected, actual);
            }

            Assert.Equal(File.ReadAllText(expected), actual);
        }
    }

    [SkippableFact]
    public void A_missing_package_or_export_fails_only_its_own_island()
    {
        var typescript = Toolchain();
        var output = Extract(
            typescript,
            Island("FixtureButton", "fixture-button"),
            Island("Absent", "not-installed"),
            Island("Nameless", "fixture-button#Nope"));

        var results = SyncExternalPropsSnapshotsTask.ReadResults(File.ReadAllText(Path.Combine(output, "result.json")));

        Assert.True(results["FixtureButton"].Ok);
        Assert.Equal("module-not-found", results["Absent"].Code);
        Assert.Equal("export-not-found", results["Nameless"].Code);
        Assert.True(File.Exists(Path.Combine(output, "FixtureButton.props.json")));
    }

    [SkippableFact]
    public void The_snapshot_does_not_depend_on_the_order_the_islands_are_listed_in()
    {
        var typescript = Toolchain();
        var forwards = File.ReadAllText(Path.Combine(
            Extract(typescript, Island("FixtureButton", "fixture-button"), Island("Badge", "fixture-button#Badge")),
            "FixtureButton.props.json"));
        var backwards = File.ReadAllText(Path.Combine(
            Extract(typescript, Island("Badge", "fixture-button#Badge"), Island("FixtureButton", "fixture-button")),
            "FixtureButton.props.json"));

        Assert.Equal(forwards, backwards);
    }

    /// <summary>Runs the extractor over <paramref name="islands" /> in a fresh output directory, returning it.</summary>
    private string Extract(string typescript, params TaskItem[] islands)
    {
        var modules = Path.Combine(_project, "node_modules");
        if (!Directory.Exists(modules))
        {
            // 'modules', not 'packages' or 'node_modules': .gitignore hides a folder by either name, and a fixture that
            // never reaches a clone fails there as a missing directory rather than as anything about the extractor.
            CopyDirectory(Path.Combine(Fixtures, "modules"), modules);
        }

        var output = Path.Combine(_project, "out-" + Guid.NewGuid().ToString("n")[..8]);
        var request = Path.Combine(output, "request.json");
        var write = new WriteExternalPropsRequestTask
        {
            BuildEngine = new RecordingEngine(),
            Islands = islands,
            TypeScriptPath = typescript,
            ProjectDirectory = _project,
            OutputDirectory = output,
            RequestPath = request,
        };
        Assert.True(write.Execute());

        var extractor = Path.Combine(RepoRoot(), "src", "Rask.External", "build", "rask-extract-props.mjs");
        Assert.True(File.Exists(extractor), $"'{extractor}' was not bundled; building Rask.External writes it.");

        using var node = Process.Start(new ProcessStartInfo("node")
        {
            ArgumentList = { extractor, request },
            WorkingDirectory = _project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var stderr = node.StandardError.ReadToEndAsync();
        var stdout = node.StandardOutput.ReadToEndAsync();
        Assert.True(node.WaitForExit(120_000), "the extractor did not finish within two minutes");
        Assert.True(node.ExitCode == 0, $"the extractor exited {node.ExitCode}: {stdout.Result}{stderr.Result}");

        return output;
    }

    /// <summary>The pinned <c>typescript.js</c>, after skipping when node or the compiler is unavailable.</summary>
    private static string Toolchain()
    {
        Skip.IfNot(NodeRuns(), "node is not installed, so the props extractor cannot run here.");

        var props = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.External", "build", "Rask.External.props"));
        var version = Regex.Match(props, "<RaskExternalTypeScriptVersion[^>]*>([^<]+)<").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(version), "Rask.External.props no longer pins RaskExternalTypeScriptVersion.");

        var engine = new RecordingEngine();
        var resolve = new ResolveTypeScriptToolTask { BuildEngine = engine, Tool = "typescript", Version = version };
        Skip.IfNot(
            resolve.Execute(),
            $"typescript@{version} is not cached and could not be fetched: {string.Join(" ", engine.Errors.Select(e => e.Message))}");

        return resolve.ToolPath;
    }

    private static TaskItem Island(string name, string module)
    {
        var item = new TaskItem(name + ".props.json");
        item.SetMetadata("IslandName", name);
        item.SetMetadata("Runtime", "react");
        item.SetMetadata("PackageModule", module);
        return item;
    }

    private static bool NodeRuns()
    {
        try
        {
            using var node = Process.Start(new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            return node is not null && node.WaitForExit(10_000) && node.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException("Could not find Rask.slnx above the test output directory.");
    }
}
