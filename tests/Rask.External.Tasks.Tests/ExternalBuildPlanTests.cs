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

        // Every runtime at once, which is the case with the most generated text in it: four plugin
        // imports, two of them shaped differently from the others.
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/a/c.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Gauge", Source = "/a/g.ts", Runtime = "lit" },
            new ExternalEntry { Name = "Panel", Source = "/a/p.vue", Runtime = "vue" },
            new ExternalEntry { Name = "Dial", Source = "/a/d.svelte", Runtime = "svelte" },
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

    [Fact]
    public void A_vue_entry_wraps_the_component_with_its_adapter()
    {
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "/app/Features/Chart.vue", Runtime = "vue" },
            "/obj/rask-external/rask");

        Assert.Contains("import Component from '/app/Features/Chart.vue'", entry, StringComparison.Ordinal);
        Assert.Contains("import { vueComponent } from '/obj/rask-external/rask/vue'", entry, StringComparison.Ordinal);
        Assert.Contains("export default vueComponent(Component)", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_svelte_entry_points_at_the_runes_adapter_module()
    {
        var entry = ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "/app/Features/Chart.svelte", Runtime = "svelte" },
            "/obj/rask-external/rask");

        Assert.Contains("import Component from '/app/Features/Chart.svelte'", entry, StringComparison.Ordinal);
        Assert.Contains("export default svelteComponent(Component)", entry, StringComparison.Ordinal);

        // Not 'rask/svelte'. The adapter keeps props reactive with a $state proxy, and a rune is a
        // COMPILER feature — it does not exist in a file the Svelte plugin does not compile. Resolving
        // to a plain .ts would leave the adapter unable to update, so every prop change would remount
        // the component and throw away its own state.
        Assert.Contains(
            "import { svelteComponent } from '/obj/rask-external/rask/svelte.svelte'",
            entry,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_runtime_is_refused_rather_than_defaulted_to_react()
    {
        // It used to fall through to React, so a typo generated a React entry for a Vue component:
        // the build succeeded, the bundle shipped, the chunk loaded, and nothing mounted.
        var ex = Assert.Throws<InvalidOperationException>(() => ExternalBuildPlan.EntryModule(
            new ExternalEntry { Name = "Chart", Source = "/app/Chart.vue", Runtime = "vue3" },
            "/obj/rask-external/rask"));

        Assert.Contains("vue3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Chart", ex.Message, StringComparison.Ordinal);

        // Names the alternatives, so the message is actionable without opening Rask's source.
        Assert.Contains("svelte", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_plugin_is_imported_only_when_its_own_runtime_is_used()
    {
        var vueOnly = Config([new ExternalEntry { Name = "Chart", Source = "/a/c.vue", Runtime = "vue" }]);

        Assert.Contains("@vitejs/plugin-vue", vueOnly, StringComparison.Ordinal);
        Assert.Contains("vue(), ", vueOnly, StringComparison.Ordinal);

        // A Vue-only app is not asked to install React's plugin, or Svelte's, to build.
        Assert.DoesNotContain("@vitejs/plugin-react", vueOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("vite-plugin-svelte", vueOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void The_svelte_plugin_is_imported_as_a_named_export()
    {
        var svelteOnly = Config([new ExternalEntry { Name = "Chart", Source = "/a/c.svelte", Runtime = "svelte" }]);

        // The shape differs per plugin and getting it wrong is a build error in generated code:
        // Svelte's is a NAMED export where React's and Vue's are defaults.
        Assert.Contains(
            "import { svelte } from '@sveltejs/vite-plugin-svelte'",
            svelteOnly,
            StringComparison.Ordinal);
        Assert.Contains("svelte(), ", svelteOnly, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plugin_order_does_not_depend_on_the_order_islands_were_discovered()
    {
        var one = Config([
            new ExternalEntry { Name = "A", Source = "/a/a.vue", Runtime = "vue" },
            new ExternalEntry { Name = "B", Source = "/a/b.tsx", Runtime = "react" },
        ]);

        var other = Config([
            new ExternalEntry { Name = "B", Source = "/a/b.tsx", Runtime = "react" },
            new ExternalEntry { Name = "A", Source = "/a/a.vue", Runtime = "vue" },
        ]);

        // The config is a bundler input, compared against what is on disk and rewritten only when it
        // differs. An order that tracked discovery would rewrite it on some builds and not others,
        // restarting the dev server's dependency graph for no reason.
        Assert.Equal(
            one[..one.IndexOf("const input", StringComparison.Ordinal)],
            other[..other.IndexOf("const input", StringComparison.Ordinal)]);
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
