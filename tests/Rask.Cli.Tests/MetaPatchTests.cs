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
        var patched = ProjectGenerator.AddRaskDevProxy(SvelteKitViteConfig);

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
        var patched = ProjectGenerator.AddRaskDevProxy(AnalogViteConfig);

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

        Assert.Equal(Existing, ProjectGenerator.AddRaskDevProxy(Existing));
    }

    [Fact]
    public void Proxying_twice_changes_nothing()
    {
        var once = ProjectGenerator.AddRaskDevProxy(SvelteKitViteConfig);

        Assert.Equal(once, ProjectGenerator.AddRaskDevProxy(once));
    }

    [Fact]
    public void An_existing_extends_is_kept_and_ours_is_added_after_it()
    {
        // SvelteKit's tsconfig extends ./.svelte-kit/tsconfig.json, which carries its whole type
        // environment. Replacing that line does not fail the build — it silently removes the
        // framework's own types, and the first error the developer sees is about their own code.
        const string SvelteKitTsConfig =
            """
            {
            	"extends": "./.svelte-kit/tsconfig.json",
            	"compilerOptions": {
            		"strict": true
            	}
            }
            """;

        var patched = ProjectGenerator.ExtendRaskTsConfig(SvelteKitTsConfig, "src/rask");

        // PARSED, not string-matched. A Contains assertion passed happily on `""extends": [...]"` —
        // malformed JSON, because the pattern had lost the opening quote of the token it was matching —
        // since the broken text still contains the good text as a substring. The scaffold shipped a
        // tsconfig no tool could read, and the test said it was fine.
        var extends = Parse(patched)["extends"] as JsonArray;

        Assert.NotNull(extends);
        Assert.Equal(
            ["./.svelte-kit/tsconfig.json", "./src/rask/tsconfig.rask.json"],
            extends!.Select(node => node!.GetValue<string>()));
    }

    /// <summary>Parses a tsconfig the way a tool would — JSONC, so comments and trailing commas.</summary>
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

    [Fact]
    public void A_tsconfig_with_no_extends_gets_one()
    {
        // Next's and TanStack's shape.
        const string Plain =
            """
            {
              "compilerOptions": {
                "strict": true
              }
            }
            """;

        var patched = ProjectGenerator.ExtendRaskTsConfig(Plain, "app/rask");
        var root = Parse(patched);

        Assert.Equal("./app/rask/tsconfig.rask.json", root["extends"]!.GetValue<string>());
        Assert.True(root["compilerOptions"]!["strict"]!.GetValue<bool>());
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

        var patched = ProjectGenerator.ExtendRaskTsConfig(Jsonc, "src/rask");

        Assert.Contains("To learn more about this file", patched, StringComparison.Ordinal);
        Assert.Contains("Angular's strictest setting", patched, StringComparison.Ordinal);
        Assert.Equal("./src/rask/tsconfig.rask.json", Parse(patched)["extends"]!.GetValue<string>());
    }

    [Fact]
    public void Extending_twice_changes_nothing()
    {
        const string Plain = """{ "compilerOptions": { "strict": true } }""";

        var once = ProjectGenerator.ExtendRaskTsConfig(Plain, "src/rask");

        Assert.Equal(once, ProjectGenerator.ExtendRaskTsConfig(once, "src/rask"));
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
