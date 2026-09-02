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

        // Every runtime that can share a project, which is the case with the most generated text in
        // it: six plugin imports, one shaped differently from the others, and two of them carrying
        // scoping options. React is left out because it cannot coexist with Preact — and Preact
        // standing in for it also proves a SCOPED JSX plugin parses.
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/a/preact/c.tsx", Runtime = "preact" },
            new ExternalEntry { Name = "Gauge", Source = "/a/lit/g.ts", Runtime = "lit" },
            new ExternalEntry { Name = "Panel", Source = "/a/p.vue", Runtime = "vue" },
            new ExternalEntry { Name = "Dial", Source = "/a/d.svelte", Runtime = "svelte" },
            new ExternalEntry { Name = "Meter", Source = "/a/solid/m.tsx", Runtime = "solid" },
            new ExternalEntry { Name = "Card", Source = "/a/ng/card.ts", Runtime = "angular" },
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
    public void Single_file_compilers_are_registered_before_the_jsx_transform()
    {
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/a/c.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Panel", Source = "/a/p.vue", Runtime = "vue" },
            new ExternalEntry { Name = "Dial", Source = "/a/d.svelte", Runtime = "svelte" },
        ]);

        var vue = config.IndexOf("vue()", StringComparison.Ordinal);
        var svelte = config.IndexOf("svelte()", StringComparison.Ordinal);
        var react = config.IndexOf("react()", StringComparison.Ordinal);

        Assert.True(vue >= 0 && svelte >= 0 && react >= 0);

        // Not cosmetic. A Vue or Svelte plugin claims one extension it alone understands; the React
        // plugin installs a GENERAL JSX transform. Registered the other way round, a .vue reaches the
        // JSX parser and the build dies with "Unexpected JSX expression" at line 1 — an error naming
        // neither Vue nor the plugin that should have handled it. Cost a real debugging round.
        Assert.True(vue < react, "the Vue plugin must be registered before the React one");
        Assert.True(svelte < react, "the Svelte plugin must be registered before the React one");
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

    [Fact]
    public void Two_jsx_runtimes_scope_their_plugins_to_their_own_directories()
    {
        var config = Config([
            new ExternalEntry { Name = "Gauge", Source = "/app/Islands/solid/Gauge.tsx", Runtime = "solid" },
            new ExternalEntry { Name = "Chart", Source = "/app/Islands/react/Chart.tsx", Runtime = "react" },
        ]);

        // Scoped by DIRECTORY, not by file — measured, not reasoned about. A file-level include
        // transforms the island and leaves every module it IMPORTS to the other plugin: a Solid island
        // importing a Row.tsx beside it built green and shipped a Preact vnode into Solid's renderer.
        Assert.Contains("solid({ include: ['/app/Islands/solid/**/*.{jsx,tsx}'] })", config, StringComparison.Ordinal);
        Assert.Contains("react({ include: ['/app/Islands/react/**/*.{jsx,tsx}'] })", config, StringComparison.Ordinal);
    }

    [Fact]
    public void One_jsx_runtime_alone_is_left_unscoped()
    {
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/app/Islands/Chart.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Panel", Source = "/app/Islands/Panel.vue", Runtime = "vue" },
        ]);

        // Nothing competes for a .tsx here, so there is nothing to disambiguate. An include would only
        // be one more thing that can be subtly wrong, and it would break the moment an island moved.
        Assert.Contains("react(), ", config, StringComparison.Ordinal);
        Assert.DoesNotContain("react({", config, StringComparison.Ordinal);
    }

    [Fact]
    public void The_angular_plugin_is_never_scoped_even_beside_a_lit_island()
    {
        var config = Config([
            new ExternalEntry { Name = "Gauge", Source = "/app/Islands/lit/Gauge.ts", Runtime = "lit" },
            new ExternalEntry { Name = "Chart", Source = "/app/Islands/ng/Chart.ts", Runtime = "angular" },
        ]);

        // Angular needs no confining: unscoped, the plugin compiles the Angular island ahead of time
        // and passes ordinary TypeScript through untouched, so the Lit element beside it is unharmed.
        // Measured with both in one bundle, by checking the Lit chunk still registers its tag and the
        // Angular chunk still carries the AOT marker. Scoping it would be a rule invented rather than
        // measured.
        Assert.DoesNotContain("include:", config, StringComparison.Ordinal);
        Assert.Contains("angular({ jit: false })", config, StringComparison.Ordinal);
    }

    [Fact]
    public void The_angular_plugin_is_told_which_tsconfig_to_read()
    {
        var config = ExternalBuildPlan.ViteConfig(
            [new ExternalEntry { Name = "Chart", Source = "/app/Islands/Chart.ts", Runtime = "angular" }],
            "/obj/entries",
            "/app/wwwroot/_rask/external",
            "/app/wwwroot/_rask/external/manifest.json",
            "/_rask/external/",
            "/obj/tsconfig.angular.build.json");

        // Left unset the plugin looks for tsconfig.app.json, WARNS that it is missing, and then builds
        // anyway with the compiler configured by nothing.
        Assert.Contains("tsconfig: '/obj/tsconfig.angular.build.json'", config, StringComparison.Ordinal);
        Assert.Contains("jit: false", config, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_angular_tsconfig_never_says_noEmit()
    {
        var json = ExternalBuildPlan.AngularTsConfig(
            [new ExternalEntry { Name = "Chart", Source = "/app/Islands/Chart.ts", Runtime = "angular" }],
            "/obj/rask-external");

        Assert.NotNull(json);

        // The bug this pins cost a real debugging round. Pointed at the app's own tsconfig — which
        // sets noEmit for its type-check — ngtsc emits nothing, and rolldown then reports
        // `"default" is not exported by <island>.ts` for EVERY .ts island in the project, naming files
        // that plainly export one and mentioning neither Angular nor noEmit.
        Assert.DoesNotContain("noEmit", json, StringComparison.Ordinal);

        // Angular's decorators are the TypeScript 4 form; Lit 3's standard decorators need this off,
        // which is why this config lists the Angular islands only.
        Assert.Contains("\"experimentalDecorators\": true", json, StringComparison.Ordinal);
        Assert.Contains("/app/Islands/Chart.ts", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_with_no_angular_island_gets_no_angular_tsconfig()
    {
        Assert.Null(ExternalBuildPlan.AngularTsConfig(
            [new ExternalEntry { Name = "Chart", Source = "/app/Islands/Chart.tsx", Runtime = "react" }],
            "/obj/rask-external"));
    }

    [Fact]
    public void React_and_preact_in_one_project_are_refused_by_name()
    {
        // Not a rule Rask chose: @vitejs/plugin-react resolves Babel 8 and @preact/preset-vite pins a
        // @babel/core@"7.x" peer, so npm refuses the install outright. Left to npm the failure is an
        // ERESOLVE tree naming four Babel packages and neither island.
        var ex = Assert.Throws<InvalidOperationException>(() => Config([
            new ExternalEntry { Name = "Chart", Source = "/app/a/Chart.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Gauge", Source = "/app/b/Gauge.tsx", Runtime = "preact" },
        ]));

        Assert.Contains("Chart", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gauge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("preact/compat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_jsx_runtimes_in_one_directory_are_refused()
    {
        // They can share a project but not a folder: the scope IS the directory, so a shared one
        // leaves both plugins claiming the same files and one island compiled by the wrong transform.
        var ex = Assert.Throws<InvalidOperationException>(() => Config([
            new ExternalEntry { Name = "Chart", Source = "/app/Islands/Chart.tsx", Runtime = "solid" },
            new ExternalEntry { Name = "Gauge", Source = "/app/Islands/Gauge.tsx", Runtime = "react" },
        ]));

        Assert.Contains("/app/Islands", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Chart", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gauge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("react", ex.Message, StringComparison.Ordinal);
        Assert.Contains("solid", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_island_directory_counts_as_the_same_tree()
    {
        // The case that looks fine and is not. React's scope becomes 'Features/Islands/**', which
        // CONTAINS Features/Islands/Solid — so the two globs overlap and React's plugin claims the
        // Solid island. Equality alone would have let this through.
        var ex = Assert.Throws<InvalidOperationException>(() => Config([
            new ExternalEntry { Name = "Chart", Source = "/app/Features/Islands/Chart.tsx", Runtime = "react" },
            new ExternalEntry { Name = "Gauge", Source = "/app/Features/Islands/Solid/Gauge.tsx", Runtime = "solid" },
        ]));

        Assert.Contains("Chart", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gauge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("do not nest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Angular_and_lit_may_share_a_directory()
    {
        // The directory rule is about SCOPED plugins, and Angular's never is. Refusing this pair would
        // have been a rule invented rather than measured — they build correctly side by side, verified
        // on the emitted chunks.
        var config = Config([
            new ExternalEntry { Name = "Badge", Source = "/app/Islands/Badge.ts", Runtime = "lit" },
            new ExternalEntry { Name = "Quote", Source = "/app/Islands/Quote.ts", Runtime = "angular" },
        ]);

        Assert.Contains("@analogjs/vite-plugin-angular", config, StringComparison.Ordinal);
        Assert.Contains("'Badge': ", config, StringComparison.Ordinal);
        Assert.Contains("'Quote': ", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_jsx_runtimes_in_separate_directories_are_allowed()
    {
        var config = Config([
            new ExternalEntry { Name = "Chart", Source = "/app/Islands/solid/Chart.tsx", Runtime = "solid" },
            new ExternalEntry { Name = "Gauge", Source = "/app/Islands/react/Gauge.tsx", Runtime = "react" },
        ]);

        Assert.Contains("vite-plugin-solid", config, StringComparison.Ordinal);
        Assert.Contains("@vitejs/plugin-react", config, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preact", "preactComponent", "@preact/preset-vite")]
    [InlineData("solid", "solidComponent", "vite-plugin-solid")]
    [InlineData("angular", "angularComponent", "@analogjs/vite-plugin-angular")]
    public void Each_new_runtime_gets_its_own_entry_and_plugin(string runtime, string factory, string plugin)
    {
        // The failure this pins is silent: a runtime the entry generator knows and the config does not
        // builds a chunk with no plugin to compile it, which is a parse error naming a line in
        // someone else's node_modules.
        var extension = runtime == "angular" ? "ts" : "tsx";
        var island = new ExternalEntry { Name = "Chart", Source = $"/app/Islands/Chart.{extension}", Runtime = runtime };

        var entry = ExternalBuildPlan.EntryModule(island, "/obj/rask-external/rask");
        Assert.Contains($"import {{ {factory} }} from '/obj/rask-external/rask/{runtime}'", entry, StringComparison.Ordinal);
        Assert.Contains($"export default {factory}(Component)", entry, StringComparison.Ordinal);

        Assert.Contains(plugin, Config([island]), StringComparison.Ordinal);
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
