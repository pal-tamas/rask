using System.Diagnostics;
using Rask.External.Tasks;

namespace Rask.External.Tasks.Tests;

// The bundler's inputs are JavaScript and TypeScript emitted from C#. Asserted as text for the
// contracts that matter, and handed to node for a parse — a config that is subtly malformed would
// otherwise surface as a bundler error in someone else's project, with the generator nowhere in the
// stack trace.
public class ExternalBuildPlanTests
{
    [Fact]
    public void A_react_entry_wraps_the_component_with_its_adapter()
    {
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "/app/Features/Chart.tsx", Runtime = "react" },
            "/obj/rask-external/rask");

        // The author's file is imported as-is: Chart.tsx stays an ordinary React component with no
        // Rask import in it, which is the whole point of generating the entry rather than asking for
        // a wrapper by hand.
        Assert.Contains("import Component from '/app/Features/Chart.tsx'", entry, StringComparison.Ordinal);
        Assert.Contains("import { reactComponent } from '/obj/rask-external/rask/react'", entry, StringComparison.Ordinal);
        Assert.Contains("export default reactComponent(Component)", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lit_entry_takes_the_tag_name_from_the_module()
    {
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Gauge", Source = "/app/widgets/gauge.ts", Runtime = "lit" },
            "/obj/rask-external/rask");

        // A custom element registers its own tag and nothing about the file reveals it, so the
        // contract is a default export naming it. Importing the module runs the registration too.
        Assert.Contains("import tag from '/app/widgets/gauge.ts'", entry, StringComparison.Ordinal);
        Assert.Contains("export default litComponent(tag)", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_paths_become_forward_slashes()
    {
        // A Windows path inside a JS string literal is a string full of escape sequences: C:\app\src
        // reads \a and \s, and the import silently resolves somewhere else rather than failing.
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = @"C:\app\src\Chart.tsx", Runtime = "react" },
            @"C:\app\obj\rask");

        Assert.DoesNotContain(@"\", entry, StringComparison.Ordinal);
        Assert.Contains("C:/app/src/Chart.tsx", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_path_becomes_a_relative_specifier_not_a_bare_one()
    {
        // The bug this pins cost a real debugging round: a relative path with no leading './' is not a
        // relative specifier, it is a BARE one, and the bundler resolves it against node_modules. The
        // error then names a package nobody wrote — here it would have been 'obj'.
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "Features/Chart.tsx", Runtime = "react" },
            "obj/Debug/net10.0/rask-external/rask");

        Assert.Contains("from './Features/Chart.tsx'", entry, StringComparison.Ordinal);
        Assert.Contains("from './obj/Debug/net10.0/rask-external/rask/react'", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_path_is_left_alone()
    {
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "/app/Chart.tsx", Runtime = "react" },
            "/app/obj/rask");

        Assert.Contains("from '/app/Chart.tsx'", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("'./", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void The_react_plugin_is_only_imported_when_a_react_island_exists()
    {
        var litOnly = Config([new ExternalEntry { Name = "Gauge", Source = "/a/g.ts", Runtime = "lit" }]);

        // So a Lit-only app is never asked to install @vitejs/plugin-react to build.
        Assert.DoesNotContain("@vitejs/plugin-react", litOnly, StringComparison.Ordinal);

        var withReact = Config([new ExternalEntry { Name = "Chart", Source = "/a/c.tsx", Runtime = "react" }]);
        Assert.Contains("@vitejs/plugin-react", withReact, StringComparison.Ordinal);
    }

    [Fact]
    public void The_config_maps_every_island_to_its_own_entry()
    {
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/a/c.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Gauge", Source = "/a/g.ts", Runtime = "lit" },
        ]);

        // One rollup input per island is what makes a chunk per island, which is what lets
        // hydrate="visible" avoid downloading anything until it is scrolled to.
        Assert.Contains("'Chart': '/obj/entries/Chart.entry.ts',", config, StringComparison.Ordinal);
        Assert.Contains("'Gauge': '/obj/entries/Gauge.entry.ts',", config, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_config_parses_as_javascript()
    {
        // The assertion that catches what string assertions cannot: a brace escaped wrong, a trailing
        // comma in the wrong place, an interpolation that swallowed a delimiter.
        var node = ResolveNode();
        if (node is null)
        {
            Console.WriteLine("ExternalBuildPlanTests: no 'node' on PATH — the parse check did NOT run.");
            return;
        }

        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/a/c.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Gauge", Source = "/a/g.ts", Runtime = "lit" },
        ]);

        var path = Path.Combine(Path.GetTempPath(), $"rask-external-{Guid.NewGuid():N}.mjs");
        File.WriteAllText(path, config);

        try
        {
            var psi = new ProcessStartInfo(node, $"--check \"{path}\"")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi)!;
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30_000);

            Assert.True(proc.ExitCode == 0, $"generated vite config does not parse:\n{stderr}\n\n{config}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Config(IReadOnlyList<ExternalEntry> islands) =>
        ExternalBuildPlan.ViteConfig(
            islands,
            "/obj/entries",
            "/app/wwwroot/_rask/external",
            "/app/wwwroot/_rask/external/manifest.json",
            "/_rask/external/");

    private static string? ResolveNode()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var names = OperatingSystem.IsWindows() ? ["node.exe", "node.cmd"] : new[] { "node" };
        foreach (var dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
