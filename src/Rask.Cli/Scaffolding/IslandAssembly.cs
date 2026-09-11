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
        var needsNode = false;

        foreach (var runtime in runtimes)
        {
            foreach (var asset in TemplateAssets.Load($"{FragmentRoot}/{runtime}"))
            {
                if (string.Equals(asset.Path, "island.json", StringComparison.Ordinal))
                {
                    needsNode |= ReadDependencies(asset.Bytes, dependencies);
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

        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "package.json"), Manifest(name, dependencies, runtimes)));
        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "eslint.config.mjs"), EsLintConfig(runtimes)));
        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, ".prettierrc"), PrettierRc));
        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, ".prettierignore"), PrettierIgnore));
        return files;
    }

    /// <summary>
    ///     The linting packages every island project gets, and the plugin each runtime adds.
    /// </summary>
    /// <remarks>
    ///     The same set the front-end templates declare, kept in step by a test: an island written in
    ///     React and a React client that lint under different rules is a project that argues with
    ///     itself. Angular's plugin is deliberately absent here as it is there — angular-eslint expects
    ///     its own builder wiring, and half-connecting someone else's linting is worse than the base.
    /// </remarks>
    private static readonly (string Package, string Version)[] LintBase =
    [
        ("eslint", "^10.10.0"),
        ("@eslint/js", "^10.0.1"),
        ("typescript-eslint", "^8.70.0"),
        ("prettier", "^3.9.6"),
        ("eslint-config-prettier", "^10.1.8"),
        ("globals", "^17.12.0"),
    ];

    private static readonly FrozenDictionary<string, (string Package, string Version)> LintPlugin =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["react"] = ("eslint-plugin-react-hooks", "^7.1.1"),
            ["preact"] = ("eslint-plugin-react-hooks", "^7.1.1"),
            ["vue"] = ("eslint-plugin-vue", "^10.11.0"),
            ["svelte"] = ("eslint-plugin-svelte", "^3.23.0"),
            ["solid"] = ("eslint-plugin-solid", "^0.18.0"),
            ["lit"] = ("eslint-plugin-lit", "^2.3.1"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    private const string PrettierRc = """
        {
          "semi": false,
          "singleQuote": true,
          "printWidth": 100,
          "trailingComma": "all"
        }

        """;

    private const string PrettierIgnore = """
        # Everything under obj/ and bin/ is generated or built — including the island prop types and the
        # Vite config Rask writes for the bundle.
        obj/
        bin/
        node_modules/
        wwwroot/_rask/

        # Written by npm; reformatting one makes every diff unreadable.
        package-lock.json

        """;

    /// <summary>The flat config, carrying a plugin for each runtime the project actually holds.</summary>
    private static string EsLintConfig(IReadOnlyList<string> runtimes)
    {
        var imports = new StringBuilder()
            .AppendLine("import js from '@eslint/js'")
            .AppendLine("import ts from 'typescript-eslint'")
            .AppendLine("import globals from 'globals'")
            .AppendLine("import prettier from 'eslint-config-prettier/flat'");

        var blocks = new StringBuilder()
            .AppendLine("  js.configs.recommended,")
            .AppendLine("  ...ts.configs.recommended,");

        if (runtimes.Any(r => r is "react" or "preact"))
        {
            imports.AppendLine("import reactHooks from 'eslint-plugin-react-hooks'");
            blocks.AppendLine("  reactHooks.configs.flat['recommended-latest'],");
        }

        if (runtimes.Contains("vue", StringComparer.Ordinal))
        {
            imports.AppendLine("import vue from 'eslint-plugin-vue'");
            blocks.AppendLine("  ...vue.configs['flat/recommended'],");
            blocks.AppendLine("  { files: ['**/*.vue'], languageOptions: { parserOptions: { parser: ts.parser } } },");
        }

        if (runtimes.Contains("svelte", StringComparer.Ordinal))
        {
            imports.AppendLine("import svelte from 'eslint-plugin-svelte'");
            blocks.AppendLine("  ...svelte.configs.recommended,");
            blocks.AppendLine("  { files: ['**/*.svelte'], languageOptions: { parserOptions: { parser: ts.parser } } },");
        }

        if (runtimes.Contains("solid", StringComparer.Ordinal))
        {
            imports.AppendLine("import solid from 'eslint-plugin-solid'");
            blocks.AppendLine("  solid.configs['flat/typescript'],");
        }

        if (runtimes.Contains("lit", StringComparer.Ordinal))
        {
            imports.AppendLine("import lit from 'eslint-plugin-lit'");
            blocks.AppendLine("  lit.configs['flat/recommended'],");
        }

        // $$ so that a single brace is literal and {{…}} interpolates: this emits JavaScript, which is
        // mostly braces, and a raw string literal does not use the {{ doubling that a plain
        // interpolated string does.
        return $$"""
            // ESLint's flat config for this project's islands. Close to the recommended sets on purpose:
            // a starter that argues about style on its first run is a starter people delete the config
            // from. eslint-config-prettier goes LAST and turns off every rule the formatter would fight.
            //
            // obj/ is ignored: the prop types and the Vite config Rask generates live there, and linting
            // generated code reports problems in files nobody may edit.
            {{imports}}
            export default ts.config(
              {
                ignores: ['obj/**', 'bin/**', 'node_modules/**', 'wwwroot/_rask/**'],
              },
            {{blocks}}  {
                languageOptions: {
                  globals: { ...globals.browser },
                },
              },
              prettier,
            )

            """;
    }

    /// <summary>The flags the templates' island regions are marked with.</summary>
    /// <remarks>
    ///     <c>islands</c> for the Rask.External reference every front-end runtime needs, and
    ///     <c>islands-blazor</c> for Rask.Blazor, which is a different package and a different kind.
    /// </remarks>
    public static IEnumerable<string> Flags(IReadOnlyList<string> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        if (runtimes.Any(r => !string.Equals(r, IslandRuntimes.Blazor, StringComparison.Ordinal)))
        {
            yield return "islands";
        }

        if (runtimes.Contains(IslandRuntimes.Blazor, StringComparer.Ordinal))
        {
            yield return "islands-blazor";
        }
    }

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

        var options = node["compilerOptions"] as JsonObject ?? [];

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

        // Lit and Angular both decorate, and they used to disagree here: Lit 3's `accessor` form needs
        // experimentalDecorators OFF while Angular needs it ON, so a project holding both islands could
        // not type-check either way. The Lit fragment is written in the legacy form instead, which
        // works under ON — one setting serves both, and there is no combination left to refuse.
        if (runtimes.Any(r => r is "lit" or "angular"))
        {
            options["experimentalDecorators"] = true;
            options["useDefineForClassFields"] = false;
        }

        node["compilerOptions"] = options;

        var include = node["include"] as JsonArray ?? [];
        foreach (var pattern in new[]
        {
            "Features/**/*.ts", "Features/**/*.tsx", "Features/**/*.vue", "Features/**/*.svelte",
        })
        {
            if (!include.Any(v => string.Equals(v?.GetValue<string>(), pattern, StringComparison.Ordinal)))
            {
                include.Add(pattern);
            }
        }

        node["include"] = include;

        return file with
        {
            Content = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
        };
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

    /// <summary>
    ///     The root manifest an island project needs: the chosen runtimes' packages, plus vite and
    ///     TypeScript, which every one of them is bundled and checked by.
    /// </summary>
    private static string Manifest(
        string name, SortedDictionary<string, string> dependencies, IReadOnlyList<string> runtimes)
    {
        foreach (var (package, version) in LintBase)
        {
            dependencies.TryAdd(package, version);
        }

        foreach (var runtime in runtimes)
        {
            if (LintPlugin.TryGetValue(runtime, out var plugin))
            {
                dependencies.TryAdd(plugin.Package, plugin.Version);
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
                ["lint"] = "eslint .",
                ["format"] = "prettier --write .",
                ["format:check"] = "prettier --check .",
            },
            ["devDependencies"] = new JsonObject(
                dependencies.Select(d => new KeyValuePair<string, JsonNode?>(d.Key, d.Value))),
        };

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
