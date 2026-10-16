using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Adds the island runtimes a scaffold asked for to the host template's files.
/// </summary>
/// <remarks>
///     <para>
///         Islands are the one thing here that is assembled rather than materialised. A template is a
///         fixed tree; islands compose — eight runtimes across three host shapes, several at once — so
///         what is committed is a FRAGMENT per runtime (<c>src/Rask.Templates/_islands/&lt;runtime&gt;/</c>)
///         and this merges the chosen ones. The component files are still copied verbatim from those
///         fragments; only the two JSON files that several runtimes share are built here.
///     </para>
///     <para>
///         <c>package.json</c> is a pure addition — a host template has none — so it is written whole.
///         <c>tsconfig.json</c> already exists (the hosts use scoped TypeScript) and needs one line:
///         the <c>extends</c> that makes <c>@rask/&lt;Name&gt;.props</c> resolve. Adding it
///         unconditionally would break every project WITHOUT islands, because the file it extends is
///         generated into obj/ by the island build and would not be there.
///     </para>
/// </remarks>
internal static class IslandAssembly
{
    /// <summary>The fragment root inside the embedded template payload.</summary>
    private const string FragmentRoot = "_islands";

    /// <summary>
    ///     The files <paramref name="runtimes"/> add to a scaffold, and the rewrite its tsconfig needs.
    /// </summary>
    /// <param name="targetDirectory">Where the project is being written.</param>
    /// <param name="runtimes">The runtimes asked for, already validated by <see cref="IslandRuntimes.Refuse"/>.</param>
    /// <param name="name">The app's name, substituted for the placeholder namespace.</param>
    /// <param name="existing">The host template's files, so tsconfig.json can be rewritten in place.</param>
    public static IReadOnlyList<ScaffoldFile> Apply(
        string targetDirectory,
        IReadOnlyList<string> runtimes,
        string name,
        IReadOnlyList<ScaffoldFile> existing)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetDirectory);
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(existing);

        if (runtimes.Count == 0)
        {
            return existing;
        }

        var files = new List<ScaffoldFile>(existing.Count + runtimes.Count * 2);
        var dependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var overrides = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var needsNode = false;

        foreach (var runtime in runtimes)
        {
            foreach (var asset in TemplateAssets.Load($"{FragmentRoot}/{runtime}"))
            {
                if (string.Equals(asset.Path, "island.json", StringComparison.Ordinal))
                {
                    needsNode |= ReadDependencies(asset.Bytes, dependencies);
                    ReadOverrides(asset.Bytes, overrides);
                    continue;
                }

                var relative = asset.Path.Replace(
                    TemplateMaterializer.NameToken, name, StringComparison.Ordinal);

                files.Add(new ScaffoldFile(
                    Path.Combine(targetDirectory, relative.Replace('/', Path.DirectorySeparatorChar)),
                    Encoding.UTF8.GetString(asset.Bytes)
                        .Replace(TemplateMaterializer.NameToken, name, StringComparison.Ordinal)));
            }
        }

        // Blazor alone needs neither: no npm dependencies, so no package.json and nothing for the
        // island build's TypeScript to resolve.
        if (!needsNode)
        {
            files.AddRange(existing);
            return files;
        }

        foreach (var file in existing)
        {
            files.Add(IsTsConfig(file) ? WithIslandPaths(file, runtimes) : file);
        }

        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "package.json"), Manifest(name, dependencies, overrides, runtimes)));
        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, ToolchainConfigFile), ToolchainConfig(runtimes)));
        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, ".vite-hooks", "pre-commit"), PreCommitHook));
        return files;
    }

    /// <summary>
    ///     The project's Vite+ config — lint, format and the pre-commit hook's staged block, and no build.
    /// </summary>
    /// <remarks>
    ///     A <c>.mjs</c>, not the <c>vite.config.ts</c> a front-end template has: every <c>.ts</c> in a host
    ///     project is a component's scoped TypeScript, and a config file paired with no component is RASK015.
    /// </remarks>
    internal const string ToolchainConfigFile = "vite.config.mjs";

    /// <summary>
    ///     The lint plugin each runtime adds to the project's Oxlint, and the rules it turns on.
    /// </summary>
    /// <remarks>
    ///     The same set each front-end template's <c>vite.config.ts</c> carries, kept in step by a test: an island
    ///     written in React and a React client that lint under different rules is a project that argues with
    ///     itself. React, Preact and Vue have plugins built into Oxlint; Solid and Lit have none, so their ESLint
    ///     plugin runs inside it as a JS plugin and is the one package a runtime adds. Svelte and Angular add
    ///     nothing: their templates are their compilers' to check, which the island build already runs.
    /// </remarks>
    private static readonly FrozenDictionary<string, LintPlugin> LintPlugins =
        new Dictionary<string, LintPlugin>(StringComparer.Ordinal)
        {
            ["react"] = new("react", null, ["'react/rules-of-hooks': 'error'", "'react/exhaustive-deps': 'warn'"]),
            ["preact"] = new("react", null, ["'react/rules-of-hooks': 'error'", "'react/exhaustive-deps': 'warn'"]),
            ["vue"] = new("vue", null, []),
            ["solid"] = new(null, "eslint-plugin-solid",
            [
                "'solid/components-return-once': 'warn'", "'solid/event-handlers': 'warn'", "'solid/imports': 'warn'",
                "'solid/jsx-no-duplicate-props': 'error'", "'solid/jsx-no-script-url': 'error'", "'solid/jsx-uses-vars': 'error'",
                "'solid/no-destructure': 'error'", "'solid/no-innerhtml': 'error'", "'solid/no-react-deps': 'warn'",
                "'solid/no-react-specific-props': 'warn'", "'solid/prefer-for': 'error'", "'solid/reactivity': 'warn'",
                "'solid/self-closing-comp': 'warn'", "'solid/style-prop': 'warn'",
            ]),
            ["lit"] = new(null, "eslint-plugin-lit",
            [
                "'lit/attribute-value-entities': 'error'", "'lit/binding-positions': 'error'",
                "'lit/no-duplicate-template-bindings': 'error'", "'lit/no-invalid-html': 'error'",
                "'lit/no-legacy-template-syntax': 'error'", "'lit/no-property-change-update': 'error'",
            ]),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>One runtime's lint: an Oxlint built-in plugin, or an ESLint plugin run as a JS plugin, and its rules.</summary>
    private sealed record LintPlugin(string? BuiltIn, string? JsPlugin, IReadOnlyList<string> Rules)
    {
        /// <summary>The name the plugin's rules are prefixed with: <c>eslint-plugin-solid</c> is <c>solid</c>.</summary>
        public string? JsPluginName => JsPlugin?["eslint-plugin-".Length..];
    }

    // Hooks run from the repository root; this project may be a folder inside it.
    private const string PreCommitHook = """
        # Hooks run from the repository root, and this project may be a folder inside it: vp staged reads
        # its staged block from the vite.config.mjs beside this folder. The PATH the hook runner adds is
        # relative to the root, so it is made absolute again once here.
        cd "$(dirname "$0")/.." || exit 1
        PATH="$PWD/node_modules/.bin:$PATH" vp staged

        """;

    /// <summary>
    ///     Where the Solid fragment puts its islands, and so the files Solid's rules are scoped to when they
    ///     would otherwise reach another runtime's.
    /// </summary>
    /// <remarks>
    ///     A Solid island and a React or Preact one are both <c>.tsx</c>, and only the <c>.cs</c> beside each says
    ///     which runtime it is — so in a project holding both, Solid's rules (no destructured props, <c>class</c>
    ///     over <c>className</c>) would fail the React component on its first <c>vp check</c>.
    /// </remarks>
    internal const string SolidFolder = "**/Solid/**";

    /// <summary>The Oxlint plugins, JS plugins and project-wide rules for <paramref name="runtimes" />.</summary>
    private static (string[] Plugins, string[] JsPlugins, string[] Rules, bool ScopeSolid) LintFor(IReadOnlyList<string> runtimes)
    {
        var chosen = runtimes
            .Select(r => (Runtime: r, Plugin: LintPlugins.GetValueOrDefault(r)))
            .Where(c => c.Plugin is not null)
            .ToList();
        var scopeSolid = runtimes.Contains("solid", StringComparer.Ordinal) && runtimes.Any(r => r is "react" or "preact");
        string[] plugins = ["typescript", "oxc", .. chosen.Select(c => c.Plugin!.BuiltIn).OfType<string>().Distinct(StringComparer.Ordinal)];
        string[] jsPlugins =
        [
            $"{{ name: 'vite-plus', specifier: '{VitePlusToolchain.Package}/oxlint-plugin' }}",
            .. chosen
                .Where(c => c.Plugin!.JsPlugin is not null)
                .Select(c => $"{{ name: '{c.Plugin!.JsPluginName}', specifier: '{c.Plugin.JsPlugin}' }}")
                .Distinct(StringComparer.Ordinal),
        ];
        string[] rules =
        [
            .. chosen
                .Where(c => !(scopeSolid && string.Equals(c.Runtime, "solid", StringComparison.Ordinal)))
                .SelectMany(c => c.Plugin!.Rules)
                .Distinct(StringComparer.Ordinal),
            "'vite-plus/prefer-vite-plus-imports': 'error'",
        ];
        return (plugins, jsPlugins, rules, scopeSolid);
    }

    /// <summary>The project's <see cref="ToolchainConfigFile" />, linting with each runtime's plugin.</summary>
    private static string ToolchainConfig(IReadOnlyList<string> runtimes)
    {
        var (plugins, jsPlugins, rules, scopeSolid) = LintFor(runtimes);
        var svelte = runtimes.Contains("svelte", StringComparer.Ordinal);

        // $$ so that a single brace is literal and {{…}} interpolates: this emits JavaScript, which is
        // mostly braces, and a raw string literal does not use the {{ doubling that a plain
        // interpolated string does.
        return $$"""
            {{(svelte ? "import { svelte } from '@sveltejs/vite-plugin-svelte'\nimport { defineConfig, lazyPlugins } from 'vite-plus'" : "import { defineConfig } from 'vite-plus'")}}

            // The lint and format half of Vite+ for this project's islands (https://viteplus.dev/guide/):
            // `vp check` reads the fmt and lint blocks, and the pre-commit hook the staged block. The
            // islands are not bundled from here — Rask writes the Vite config that does that under obj/ on
            // every build. Not type-aware: `dotnet build` type-checks every island already.
            //
            // Both blocks reach Features/ and nothing else: the islands are written there, and the rest of
            // the project is the C# side's, formatted by dotnet format and .editorconfig.
            export default defineConfig({
            {{(svelte ? SveltePlugins : "")}}  fmt: {
                semi: false,
                singleQuote: true,
                printWidth: 100,
                trailingComma: 'all',
                ignorePatterns: ['/*', '!/Features/'],
              },
              lint: {
                ignorePatterns: ['/*', '!/Features/'],
                plugins: [{{string.Join(", ", plugins.Select(p => $"'{p}'"))}}],
                jsPlugins: [
            {{Lines(jsPlugins, "      ")}}
                ],
                rules: {
            {{Lines(rules, "      ")}}
                },
            {{(scopeSolid ? SolidOverride() : "")}}  },
              staged: {
                'Features/**/*.{js,mjs,cjs,ts,tsx,jsx,vue,svelte}': 'vp check --fix',
              },
            })

            """;
    }

    // svelte-check reads the Svelte config from the nearest vite.config: without the plugin named here it
    // finds this file, sees no Svelte, and fails the island build's type check.
    private const string SveltePlugins = """
          // Not to bundle: svelte-check reads the Svelte config from the nearest vite.config, which is this one.
          plugins: lazyPlugins(() => [svelte()]),

        """;

    private static string SolidOverride() => $$"""
            // Solid and React islands are both .tsx; Solid's rules stay on the Solid ones (IslandAssembly.SolidFolder).
            overrides: [
              {
                files: ['{{SolidFolder}}'],
                rules: {
        {{Lines(LintPlugins["solid"].Rules, "          ")}}
                },
              },
            ],

        """;

    private static string Lines(IEnumerable<string> entries, string indent) =>
        string.Join("\n", entries.Select(e => $"{indent}{e},"));

    /// <summary>The flags the templates' island regions are marked with.</summary>
    /// <remarks>
    ///     <c>islands</c> for the Rask.External reference every front-end runtime needs, and
    ///     <c>islands-blazor</c> for Rask.Blazor, which is a different package and a different kind.
    ///     <c>islands-any</c> and one <c>islands-&lt;runtime&gt;</c> per chosen runtime are what the home
    ///     page renders by: a scaffolded island nothing renders is files the user never sees working.
    /// </remarks>
    public static IEnumerable<string> Flags(IReadOnlyList<string> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        return FlagsIterator(runtimes);
    }

    private static IEnumerable<string> FlagsIterator(IReadOnlyList<string> runtimes)
    {
        if (runtimes.Any(r => !string.Equals(r, IslandRuntimes.Blazor, StringComparison.Ordinal)))
        {
            yield return "islands";
        }

        if (runtimes.Count > 0)
        {
            yield return "islands-any";
        }

        // islands-blazor is one of these, and means what it always has: the Blazor runtime was chosen.
        foreach (var runtime in runtimes)
        {
            yield return "islands-" + runtime;
        }
    }

    private static readonly string[] IslandIncludePatterns =
        ["Features/**/*.ts", "Features/**/*.tsx", "Features/**/*.vue", "Features/**/*.svelte"];

    private static bool IsTsConfig(ScaffoldFile file) =>
        string.Equals(Path.GetFileName(file.Path), "tsconfig.json", StringComparison.Ordinal);

    /// <summary>
    ///     The host's tsconfig with the generated prop-type mapping added.
    /// </summary>
    /// <remarks>
    ///     A fragment under obj/ rather than settings written here, because nothing generated is
    ///     committed: a fresh clone shows the import unresolved until the first build, which is the
    ///     honest state. The include list gains the island extensions so the front-end files are part
    ///     of the program that type-checks them.
    /// </remarks>
    private static ScaffoldFile WithIslandPaths(ScaffoldFile file, IReadOnlyList<string> runtimes)
    {
        var node = JsonNode.Parse(
            file.Content,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject ?? [];

        node["extends"] = "./obj/rask-external/tsconfig.paths.json";
        node["compilerOptions"] = WithIslandCompilerOptions(node["compilerOptions"] as JsonObject ?? [], runtimes);

        var include = node["include"] as JsonArray ?? [];
        var missing = IslandIncludePatterns
            .Where(pattern => !include.Any(v => string.Equals(v?.GetValue<string>(), pattern, StringComparison.Ordinal)))
            .ToArray();
        foreach (var pattern in missing)
        {
            include.Add(pattern);
        }

        node["include"] = include;

        return file with
        {
            Content = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
        };
    }

    private static JsonObject WithIslandCompilerOptions(JsonObject options, IReadOnlyList<string> runtimes)
    {

        // Without a jsx setting the island type-check cannot compile a .tsx at ALL, so a React, Preact
        // or Solid island fails its first build with an error about the syntax rather than the config.
        // react-jsx for all three: only the TYPE check happens here — each runtime's own Vite plugin
        // does the transform — and it is the setting the showcase type-checks React and Solid under.
        if (runtimes.Any(IslandRuntimes.Jsx.Contains))
        {
            options["jsx"] = "react-jsx";
        }

        // Preact's JSX resolves through preact rather than react, and its adapter is compiled against
        // preact's own types.
        if (runtimes.Contains("preact", StringComparer.Ordinal))
        {
            options["jsxImportSource"] = "preact";
        }

        // svelte-check needs the ambient declarations for a .svelte import to resolve.
        if (runtimes.Contains("svelte", StringComparer.Ordinal))
        {
            var types = options["types"] as JsonArray ?? [];
            if (!types.Any(t => string.Equals(t?.GetValue<string>(), "svelte", StringComparison.Ordinal)))
            {
                types.Add("svelte");
            }

            options["types"] = types;
        }

        // Angular's decorators are the legacy form. The Lit fragment declares `static properties`
        // instead of decorating, so it type-checks under either setting and the two share a project.
        if (runtimes.Contains("angular", StringComparer.Ordinal))
        {
            options["experimentalDecorators"] = true;
            options["useDefineForClassFields"] = false;
        }

        return options;
    }

    private static bool ReadDependencies(byte[] islandJson, SortedDictionary<string, string> into)
    {
        using var document = JsonDocument.Parse(islandJson);
        if (!document.RootElement.TryGetProperty("devDependencies", out var deps))
        {
            return false;
        }

        var any = false;
        foreach (var entry in deps.EnumerateObject())
        {
            // First writer wins, and the fragments agree by construction — a version test asserts it,
            // because two runtimes disagreeing about vite is an install that resolves one of them.
            into.TryAdd(entry.Name, entry.Value.GetString() ?? "");
            any = true;
        }

        return any;
    }

    // A fragment's npm `overrides`: the versions it forces on packages its runtime pulls in transitively,
    // for the stretch between a dependency's advisory and the runtime release that picks up the fix.
    private static void ReadOverrides(byte[] islandJson, SortedDictionary<string, string> into)
    {
        using var document = JsonDocument.Parse(islandJson);
        if (!document.RootElement.TryGetProperty("overrides", out var overrides))
        {
            return;
        }

        foreach (var entry in overrides.EnumerateObject())
        {
            into.TryAdd(entry.Name, entry.Value.GetString() ?? "");
        }
    }

    /// <summary>
    ///     The root manifest an island project needs: the chosen runtimes' packages, plus the Vite+
    ///     toolchain and TypeScript, which every one of them is bundled and checked by.
    /// </summary>
    private static string Manifest(
        string name,
        SortedDictionary<string, string> dependencies,
        SortedDictionary<string, string> overrides,
        IReadOnlyList<string> runtimes)
    {
        // Vite itself, which the island build runs as `npx vite` over the config it generates under obj/, and
        // vite-plus beside it for check, lint, format and the pre-commit hook. Not the alias a front-end template
        // installs (VitePlusToolchain): the core it points `vite` at ships no `vite` command for that build to run.
        dependencies.TryAdd(VitePlusToolchain.Vite, FrontEndVersions.Latest);
        dependencies.TryAdd(VitePlusToolchain.Package, FrontEndVersions.Latest);

        foreach (var runtime in runtimes)
        {
            if (LintPlugins.GetValueOrDefault(runtime)?.JsPlugin is { } plugin)
            {
                dependencies.TryAdd(plugin, FrontEndVersions.Latest);
            }
        }

        var manifest = new JsonObject
        {
            ["name"] = TemplateMaterializer.Slug(name),
            ["private"] = true,
            ["type"] = "module",
            ["description"] =
                "Island dependencies. Rask discovers each .cs/front-end pair, generates its prop types "
                + "and bundles it with Vite; there is no entry point here and nothing to run by hand.",
            ["scripts"] = new JsonObject
            {
                ["check"] = "vp check",
                ["lint"] = "vp lint",
                ["format"] = "vp fmt",
                ["format:check"] = "vp fmt --check",
                // Points git at .vite-hooks/, so the pre-commit hook runs `vp staged`; a no-op outside a repository.
                ["prepare"] = "vp config",
            },
            ["devDependencies"] = new JsonObject(
                dependencies.Select(d => new KeyValuePair<string, JsonNode?>(d.Key, d.Value))),
        };

        if (overrides.Count > 0)
        {
            manifest["overrides"] = new JsonObject(
                overrides.Select(o => new KeyValuePair<string, JsonNode?>(o.Key, o.Value)));
        }

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
