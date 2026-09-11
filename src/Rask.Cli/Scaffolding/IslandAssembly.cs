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

        files.Add(new ScaffoldFile(Path.Combine(targetDirectory, "package.json"), Manifest(name, dependencies)));
        return files;
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
    private static string Manifest(string name, SortedDictionary<string, string> dependencies)
    {
        dependencies.TryAdd("typescript", "^5.9.3");
        dependencies.TryAdd("vite", "^8.2.2");

        var manifest = new JsonObject
        {
            ["name"] = TemplateMaterializer.Slug(name),
            ["private"] = true,
            ["type"] = "module",
            ["description"] =
                "Island dependencies. Rask discovers each .cs/front-end pair, generates its prop types "
                + "and bundles it with Vite; there is no entry point here and nothing to run by hand.",
            ["devDependencies"] = new JsonObject(
                dependencies.Select(d => new KeyValuePair<string, JsonNode?>(d.Key, d.Value))),
        };

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
