using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.TestFiles;

namespace Rask.External.Tasks.Tests;

/// <summary>
///     The island dev server does not watch the project's build output, except the directory Rask generates
///     its modules into.
/// </summary>
/// <remarks>
///     Vite watches its whole root and answers a changed <c>tsconfig.json</c> or <c>.html</c> with a full page
///     reload. A WASM build copies <c>tsconfig.json</c> into <c>bin/</c> and writes HTML placeholders under
///     <c>obj/</c>, so under <c>rask dev</c> every rebuild reloaded the page instead of letting Fast Refresh keep
///     the island's state (#1197). The generated <c>ignored</c> predicate is run by node here, against the paths a
///     watcher reports, from a project directory that itself sits under a folder named <c>bin</c>.
/// </remarks>
public sealed partial class DevServerWatchTests
{
    [Fact]
    public async Task The_dev_server_ignores_build_output_but_watches_sources_and_the_generated_modules()
    {
        var node = ExternalBuildPlanTests.ResolveNode();
        Assert.SkipWhen(node is null, "no 'node' on PATH, so the generated watch predicate cannot be run.");
        var temp = Path.Combine(Path.GetTempPath(), "rask-dev-watch", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(temp, "bin", "App");
        var entries = Path.Combine(project, "obj", "Debug", "net10.0-browser", "rask-external", "entries");
        Directory.CreateDirectory(entries);
        var config = ExternalBuildPlan.ViteConfig(
            [new ExternalEntry { Name = "Chart", Source = Path.Combine(project, "Chart.tsx"), Runtime = "react" }],
            entries,
            Path.Combine(project, "wwwroot", "_rask", "external"),
            Path.Combine(project, "wwwroot", "_rask", "external", "manifest.json"),
            "/_rask/external/",
            null,
            "http://localhost:5174");
        string[] paths =
        [
            "Features/Chart.tsx",
            "Features/bin/Helper.ts",
            "bin",
            "bin/Debug/net10.0-browser/tsconfig.json",
            "obj",
            "obj/Debug",
            "obj/Debug/net10.0-browser/staticwebassets/htmlassetplaceholders/build/page.html",
            "obj/Debug/net10.0-browser/rask-external",
            "obj/Debug/net10.0-browser/rask-external/entries/Chart.entry.ts",
            "obj/Debug/net10.0-browser/rask-external/types/routes.ts",
        ];

        Dictionary<string, bool> ignored;
        try
        {
            ignored = await Ignored(node!, config, project, paths);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }

        Assert.False(ignored["Features/Chart.tsx"]);
        Assert.False(ignored["Features/bin/Helper.ts"]);
        Assert.True(ignored["bin"]);
        Assert.True(ignored["bin/Debug/net10.0-browser/tsconfig.json"]);
        Assert.True(ignored["obj/Debug/net10.0-browser/staticwebassets/htmlassetplaceholders/build/page.html"]);
        // The path down to the generated modules has to stay watched, or the watcher never descends to them.
        Assert.False(ignored["obj"]);
        Assert.False(ignored["obj/Debug"]);
        Assert.False(ignored["obj/Debug/net10.0-browser/rask-external"]);
        Assert.False(ignored["obj/Debug/net10.0-browser/rask-external/entries/Chart.entry.ts"]);
        Assert.False(ignored["obj/Debug/net10.0-browser/rask-external/types/routes.ts"]);
    }

    /// <summary>
    ///     Runs the config's <c>server.watch.ignored</c> predicate in node, from <paramref name="project" />, over
    ///     <paramref name="paths" /> made absolute the way the watcher reports them: under the real working directory.
    /// </summary>
    private static async Task<Dictionary<string, bool>> Ignored(
        string node, string config, string project, string[] paths)
    {
        var predicate = IgnoredPredicate().Match(config);
        Assert.True(predicate.Success, $"no watch predicate in the dev config:\n{config}");
        var script = Path.Combine(project, "probe.mjs");
        await File.WriteAllTextAsync(
            script,
            $$"""
              import { realpathSync } from 'node:fs'
              const ignored = {{predicate.Groups["fn"].Value}}
              const root = process.cwd().replace(/\\/g, '/')
              const paths = JSON.parse(process.argv[2])
              console.log(JSON.stringify(Object.fromEntries(paths.map(p => [p, ignored(root + '/' + p)]))))
              """,
            TestContext.Current.CancellationToken);

        var run = await TestProcess.Run(
            node,
            [script, JsonSerializer.Serialize(paths)],
            workingDirectory: project,
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(run.ExitCode == 0, $"the watch predicate did not run:\n{run.StandardError}\n\n{config}");
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(run.StandardOutput)!;
    }

    [GeneratedRegex(@"ignored: (?<fn>\(\(keep, root\) => .*?\+ '/'\)),", RegexOptions.Singleline)]
    private static partial Regex IgnoredPredicate();
}
