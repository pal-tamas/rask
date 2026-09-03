using System.Text.Json;
using System.Text.Json.Nodes;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The two edits Rask makes to what a meta framework's own creator wrote.
/// </summary>
/// <remarks>
///     The fixtures here are not invented: they are what <c>sv create</c>, <c>create-analog</c> and
///     <c>@tanstack/cli</c> actually write today, kept verbatim so that a patch which works on a
///     plausible file but not a real one fails here rather than in someone's scaffold.
/// </remarks>
public sealed class MetaPatchTests
{
    /// <summary>What <c>sv create --add sveltekit-adapter=adapter:node</c> writes.</summary>
    private const string SvelteKitViteConfig =
        """
        import adapter from '@sveltejs/adapter-node';
        import { sveltekit } from '@sveltejs/kit/vite';
        import { defineConfig } from 'vite';

        export default defineConfig({
        	plugins: [
        		sveltekit({
        			compilerOptions: {
        				runes: ({ filename }) => filename.split(/[/\\]/).includes('node_modules') ? undefined : true
        			},
        			adapter: adapter()
        		})
        	]
        });
        """;

    /// <summary>What <c>create-analog</c> writes: a FUNCTION of the Vite mode, not an object.</summary>
    private const string AnalogViteConfig =
        """
        /// <reference types="vitest" />

        import { defineConfig } from 'vite';
        import analog from '@analogjs/platform';

        // https://vitejs.dev/config/
        export default defineConfig(({ mode }) => ({
          build: {
            target: ['es2020'],
          },
          plugins: [
            analog(),
          ],
        }));
        """;

    [Fact]
    public void The_proxy_goes_into_the_object_a_plain_config_returns()
    {
        var patched = ProjectGenerator.AddRaskViteConfig(SvelteKitViteConfig, MetaTemplate.SvelteKit);

        Assert.Contains("'/_rask'", patched, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5000", patched, StringComparison.Ordinal);

        // Everything the creator did is still there. This file is where SvelteKit's node adapter lives
        // — modern SvelteKit configures kit through the Vite plugin and writes no svelte.config.js at
        // all — so losing it would mean a build that produces nothing this host can run.
        Assert.Contains("adapter: adapter()", patched, StringComparison.Ordinal);
        Assert.Contains("runes:", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void The_proxy_goes_into_the_returned_object_of_a_function_config()
    {
        var patched = ProjectGenerator.AddRaskViteConfig(AnalogViteConfig, MetaTemplate.Analog);

        // The trap this exists for: Analog's config is `defineConfig(({ mode }) => ({ … }))`, so the
        // first brace after defineConfig( is the DESTRUCTURING one. Inserting there writes the proxy
        // into the parameter list, which still looks fine and does not compile.
        var destructuring = patched.IndexOf("({ mode })", StringComparison.Ordinal);
        var proxy = patched.IndexOf("'/_rask'", StringComparison.Ordinal);

        Assert.True(destructuring >= 0, "the parameter list was rewritten");
        Assert.True(proxy > destructuring, "the proxy landed in the parameter list");
        Assert.Contains("analog(),", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_that_already_declares_a_server_block_is_left_alone()
    {
        // Appending a second `server:` is not a merge — it is a duplicate key, which TypeScript
        // rejects. Not patching is the better failure: the scaffold says so, and the app compiles.
        const string Existing =
            """
            import { defineConfig } from 'vite'

            export default defineConfig({
              server: { port: 4000 }
            })
            """;

        Assert.Equal(Existing, ProjectGenerator.AddRaskViteConfig(Existing, MetaTemplate.SolidStart));
    }

    [Fact]
    public void Proxying_twice_changes_nothing()
    {
        var once = ProjectGenerator.AddRaskViteConfig(SvelteKitViteConfig, MetaTemplate.SvelteKit);

        Assert.Equal(once, ProjectGenerator.AddRaskViteConfig(once, MetaTemplate.SvelteKit));
    }

    [Fact]
    public void The_mapping_joins_the_app_own_paths_instead_of_replacing_them()
    {
        // The bug this replaced: Rask added `extends: "./src/rask/tsconfig.rask.json"`, and TypeScript
        // does NOT merge `paths` across an extends chain — the inheriting file's entry replaces the
        // inherited one outright. create-next-app, @tanstack/cli and create-solid all write their own
        // paths into this very file, so `@rask/client` resolved to nothing. Proved with tsc before this
        // was changed: "Cannot find module '@rask/browser/geolocation'".
        const string WithPaths =
            """
            {
              "compilerOptions": {
                "strict": true,
                "paths": {
                  "@/*": ["./src/*"],
                  "#/*": ["./src/*"]
                }
              }
            }
            """;

        var patched = ProjectGenerator.AddRaskTsConfigPaths(WithPaths, "src/rask");
        var paths = Parse(patched)["compilerOptions"]!["paths"]!.AsObject();

        Assert.Equal(
            ["./src/rask/*"], paths["@rask/*"]!.AsArray().Select(node => node!.GetValue<string>()));

        // The app's own aliases are still there. Losing them is not a compile error in Rask's code — it
        // is a compile error in the developer's, on imports they never touched.
        Assert.Equal(["./src/*"], paths["@/*"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal(["./src/*"], paths["#/*"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void A_tsconfig_with_no_paths_gets_one_inside_compilerOptions()
    {
        // Analog's shape: an extends, and compilerOptions with no paths of its own. The mapping must
        // land INSIDE compilerOptions — a `paths` at the root of a tsconfig is ignored in silence.
        const string ExtendingTsConfig =
            """
            {
            	"extends": "./tsconfig.base.json",
            	"compilerOptions": {
            		"strict": true
            	}
            }
            """;

        var patched = ProjectGenerator.AddRaskTsConfigPaths(ExtendingTsConfig, "src/rask");
        var root = Parse(patched);

        Assert.Equal(
            ["./src/rask/*"],
            root["compilerOptions"]!["paths"]!["@rask/*"]!.AsArray().Select(n => n!.GetValue<string>()));

        // And its own extends is untouched. Appending ours to that chain was the other half of the
        // original bug — an inherited paths is replaced, not merged, so whatever the base config mapped
        // would have stopped resolving.
        Assert.Equal("./tsconfig.base.json", root["extends"]!.GetValue<string>());
    }

    [Fact]
    public void The_comments_in_a_jsonc_tsconfig_survive()
    {
        // Angular's tsconfig — and so Analog's — is JSONC, full of explanatory comments and links. A
        // parse-and-reserialise patch deletes every one of them while reformatting a file the developer
        // owns, and nothing fails to warn about it.
        const string Jsonc =
            """
            {
              /* To learn more about this file see: https://angular.dev/reference/configs/tsconfig. */
              "compileOnSave": false,
              "compilerOptions": {
                // Angular's strictest setting, and one Rask's own modules had to be fixed for.
                "noPropertyAccessFromIndexSignature": true
              }
            }
            """;

        var patched = ProjectGenerator.AddRaskTsConfigPaths(Jsonc, "src/rask");

        Assert.Contains("To learn more about this file", patched, StringComparison.Ordinal);
        Assert.Contains("Angular's strictest setting", patched, StringComparison.Ordinal);
        Assert.NotNull(Parse(patched)["compilerOptions"]!["paths"]!["@rask/*"]);
    }

    [Fact]
    public void Mapping_twice_changes_nothing()
    {
        const string Plain = """{ "compilerOptions": { "strict": true } }""";

        var once = ProjectGenerator.AddRaskTsConfigPaths(Plain, "src/rask");

        Assert.Equal(once, ProjectGenerator.AddRaskTsConfigPaths(once, "src/rask"));
    }

    [Fact]
    public void SvelteKit_gets_its_alias_through_kit_because_it_generates_its_own_tsconfig()
    {
        var patched = ProjectGenerator.AddRaskViteConfig(SvelteKitViteConfig, MetaTemplate.SvelteKit);

        // Measured, not assumed: with a hand-written tsconfig `paths` instead, `svelte-check` answers
        // "You have specified a baseUrl and/or paths in your tsconfig.json which interferes with
        // SvelteKit's auto-generated tsconfig.json … For path aliases, use `kit.alias` instead" — and
        // the entry displaces the generated $lib, so `import ... from '$lib/x'` stops resolving.
        // At the plugin options' top level, because that object IS the KitConfig: a `kit` key there is
        // rejected with "Object literal may only specify known properties, and 'kit' does not exist in
        // type 'KitConfig & …'" — which is a build failure, not a warning.
        Assert.Contains("alias: { '@rask': 'src/rask' }", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("kit: {", patched, StringComparison.Ordinal);

        // Not a Vite alias: that would bundle correctly and never reach the generated tsconfig.
        Assert.DoesNotContain("resolve:", patched, StringComparison.Ordinal);

        // And the adapter it was inserted beside survives.
        Assert.Contains("adapter: adapter()", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_with_no_resolve_of_its_own_gets_a_bundler_alias()
    {
        // SolidStart's shape. A tsconfig `paths` entry is a type-checking concept and Vite does not read
        // one, so without this the import type-checks and the BUILD fails to resolve it.
        const string Plain =
            """
            import { defineConfig } from 'vite'

            export default defineConfig({
              plugins: []
            })
            """;

        var patched = ProjectGenerator.AddRaskViteConfig(Plain, MetaTemplate.SolidStart);

        Assert.Contains("'@rask/'", patched, StringComparison.Ordinal);

        // fileURLToPath, not import.meta.dirname — the latter is untyped under a config whose `types`
        // are vite/client only, and a checker reports it as an error in a file nobody wrote by hand.
        Assert.Contains("import { fileURLToPath } from 'node:url'", patched, StringComparison.Ordinal);
        Assert.DoesNotContain("import.meta.dirname", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void A_config_that_already_resolves_tsconfig_paths_is_not_given_a_second_resolve()
    {
        // TanStack sets `resolve: { tsconfigPaths: true }` and Analog's plugin brings
        // vite-tsconfig-paths, so both already map it from the tsconfig — and a second `resolve` key is
        // a duplicate rather than a merge, which TypeScript rejects outright.
        const string HasResolve =
            """
            import { defineConfig } from 'vite'

            export default defineConfig({
              resolve: { tsconfigPaths: true },
              plugins: []
            })
            """;

        var patched = ProjectGenerator.AddRaskViteConfig(HasResolve, MetaTemplate.TanStackStart);

        Assert.DoesNotContain("'@rask/'", patched, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(patched, "resolve:"));
        Assert.Contains("'/_rask'", patched, StringComparison.Ordinal);
    }

    /// <summary>Parses a tsconfig the way a tool would — JSONC, so comments and trailing commas.</summary>
    /// <remarks>
    ///     Parsed, never string-matched. A <c>Contains</c> assertion once passed on malformed output —
    ///     the broken text contained the good text as a substring — and the scaffold shipped a tsconfig
    ///     no tool could read while the suite stayed green.
    /// </remarks>
    private static JsonObject Parse(string tsConfig)
    {
        var node = JsonNode.Parse(
            tsConfig,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        return Assert.IsType<JsonObject>(node);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void The_ignore_line_names_the_directory_this_framework_actually_uses()
    {
        // Nuxt and Next keep source in app/, so an ignore line naming src/rask/ matches nothing — and
        // the generated contracts, rewritten on every build, get committed.
        var nuxt = ProjectGenerator.IgnoreGeneratedDirectory("node_modules\n", "app/rask/", "Rask.Meta.Hosting");

        Assert.Contains("app/rask/", nuxt, StringComparison.Ordinal);
        Assert.Equal(nuxt, ProjectGenerator.IgnoreGeneratedDirectory(nuxt, "app/rask/", "Rask.Meta.Hosting"));
    }
}
