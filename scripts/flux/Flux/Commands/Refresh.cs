using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rask.Flux.Commands;

/// <summary>
///     Refreshes tests/Rask.Ui.Tests/Flux/flux.snapshot.json from Flux UI's public documentation.
/// </summary>
/// <remarks>
///     Rask.Ui mirrors Flux's component catalogue: its names, its props and the values each prop takes. Flux serves every
///     docs page as markdown (<c>&lt;page&gt;.md</c>, indexed by /llms.txt) and ends each one with a "Reference" section in
///     one regular shape, so the catalogue can be read rather than retyped.
///     <para>
///         A part Flux uses in its examples but heads no Reference entry with (<c>flux:navmenu</c>, <c>flux:spacer</c>,
///         <c>flux:chart.bar</c>) is read from where Flux does document it: the attributes its examples write, and a list
///         of the items a component takes (the editor's toolbar items). Such a part has no <c>props</c>: it has
///         <c>examples</c>, the props written on it with the literal <c>values</c> written, never <c>options</c> (an
///         example shows a value, it does not close the list), and an empty list when it is written bare. So a reader tells
///         it from a headed part: parity holds a page to its headed parts only.
///     </para>
///     <para>
///         What is kept is NAMES AND VALUES ONLY — the component, its props/slots/events, the options a prop accepts and
///         its default. No description is copied: those are Flux's prose. And this reads the DOCS, never the
///         <c>livewire/flux</c> repository — that source is proprietary and Rask.Ui is written without it.
///     </para>
///     <code>
///     flux refresh                (fetches fluxui.dev)
///     flux refresh &lt;dir&gt;          (reads &lt;dir&gt;/idx-llms.txt and the pages saved beside it)
///     flux refresh &lt;dir&gt; &lt;out&gt;    (and writes &lt;out&gt; instead of the committed snapshot)
///     </code>
/// </remarks>
[FluxCommand("refresh", "the catalogue snapshot (flux.snapshot.json) from Flux's markdown docs")]
public sealed partial class Refresh : IFluxCommand
{
    private const string Origin = "https://fluxui.dev";

    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["prop"] = "props",
        ["slot"] = "slots",
        ["event"] = "events",
        ["attribute"] = "attributes",
        ["data attributes"] = "attributes",
        ["css"] = "css",
        ["class"] = "classes",
        ["size"] = "sizes",
        ["css variable"] = "cssVariables",
        ["css variables"] = "cssVariables",
        ["method"] = "methods",
        ["static method"] = "staticMethods",
        ["parameter"] = "parameters",
        ["component"] = "components",
        ["livewire directive"] = "livewireDirectives",
    };

    private static readonly CompareInfo English = CultureInfo.GetCultureInfo("en").CompareInfo;

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var cache = args.Length > 0 && args[0].Length > 0 ? args[0] : null;
        var output = args.Length > 1 && args[1].Length > 0 ? Path.GetFullPath(args[1]) : Repo.Path("tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json");
        using var http = new HttpClient();

        // A page saved by hand is named after its path: components/button.md -> components-button.md.
        async Task<string> Read(string path) => cache is not null
            ? await File.ReadAllTextAsync(Path.Combine(cache, path == "llms.txt" ? "idx-llms.txt" : MeasureCommand.ReplaceFirst(path, "/", "-")))
            : await http.GetStringAsync($"{Origin}/{path}");

        var index = await Read("llms.txt");
        var pages = IndexEntry().Matches(index).Select(m => (path: m.Groups[1].Value, kind: m.Groups[2].Value, slug: m.Groups[3].Value)).ToList();

        var snapshotPages = new JsonArray();
        var snapshot = new JsonObject { ["source"] = Origin, ["pages"] = snapshotPages };
        var markdowns = new List<string>();
        foreach (var (path, kind, slug) in pages)
        {
            var markdown = await Read(path);
            markdowns.Add(markdown);
            snapshotPages.Add(new JsonObject { ["kind"] = kind, ["slug"] = slug, ["title"] = Title(markdown), ["parts"] = Reference(markdown) });
        }

        // Which parts no page heads is only known once every page's Reference is read.
        var headed = snapshotPages.SelectMany(page => (page!["parts"] as JsonArray)!.Select(part => Js.Str(part!["name"])!)).Distinct(StringComparer.Ordinal).ToList();
        for (var i = 0; i < snapshotPages.Count; i++)
        {
            var parts = (JsonArray)snapshotPages[i]!["parts"]!;
            foreach (var part in Unlisted(markdowns[i], Js.Str(snapshotPages[i]!["slug"])!, headed))
            {
                parts.Add(part);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, Js.Stringify(snapshot, 2) + "\n");
        var count = snapshotPages.Sum(p => (p!["parts"] as JsonArray)!.Count);
        Console.WriteLine($"flux snapshot: {snapshotPages.Count} pages, {count} parts -> {output}");
        return 0;
    }

    private static string Title(string markdown) => TitleLine().Match(markdown) is { Success: true } m ? m.Groups[1].Value.Trim() : "";

    // The Reference section: `### flux:button`, then `**Prop:**` / `**Slot:**` / … headings, each followed
    // by a list of "- `name` - description. Options: `a`, `b`. Default: `a`." entries.
    private static JsonArray Reference(string markdown)
    {
        var parts = new JsonArray();
        var start = ReferenceHeading().Match(markdown);
        if (!start.Success)
        {
            return parts;
        }

        JsonObject? part = null;
        string? section = null;
        foreach (var line in markdown[start.Index..].Split('\n').Skip(1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var heading = PartHeading().Match(line);
            if (heading.Success)
            {
                part = new JsonObject { ["name"] = heading.Groups[1].Value.Trim() };
                parts.Add(part);
                section = null;
                continue;
            }

            var kind = KindHeading().Match(line);
            if (kind.Success && part is not null)
            {
                section = Key(kind.Groups[1].Value);
                if (part[section] is null)
                {
                    part[section] = new JsonArray();
                }

                continue;
            }

            var entry = EntryLine().Match(line);
            if (entry.Success && part is not null && section is not null)
            {
                ((JsonArray)part[section]!).Add(Describe(entry.Groups[1].Value, entry.Groups[2].Success ? entry.Groups[2].Value : ""));
            }
        }

        return parts;
    }

    // Flux spells a few of these two ways ("CSS Variable", "CSS variables"); a heading this table does not
    // know stops the refresh rather than inventing a key nothing reads.
    private static string Key(string heading) =>
        Keys.TryGetValue(heading.Trim().ToLowerInvariant(), out var known) ? known : throw new FluxExit(1, $"flux snapshot: unknown reference heading \"{heading}\"");

    private static JsonObject Describe(string name, string text)
    {
        var entry = new JsonObject { ["name"] = name };

        // Options run to the end of their sentence; a value tagged "(default)" is the default.
        var options = OptionsSentence().Match(text);
        if (options.Success && options.Groups[1].Value.Length > 0)
        {
            var listed = options.Groups[1].Value;
            entry["options"] = new JsonArray([.. Ticked().Matches(listed).Select(m => (JsonNode?)m.Groups[1].Value)]);
            var tagged = TaggedDefault().Match(listed);
            if (tagged.Success)
            {
                entry["default"] = tagged.Groups[1].Value;
            }
        }

        var stated = StatedDefault().Match(text);
        if (stated.Success)
        {
            entry["default"] = stated.Groups[1].Value;
        }

        if (IfBoolean().IsMatch(text) && entry["options"] is null)
        {
            entry["boolean"] = true;
        }

        return entry;
    }

    // A Reference heads a part when it names it — `flux:icon.*` names `flux:icon` and every `flux:icon.<name>` —
    // or names it with a dot fewer, as the chart page's `flux:chart.summaryvalue` names `flux:chart.summary.value`.
    private static bool IsHeaded(string name, IReadOnlyList<string> headed)
    {
        static string Undotted(string text) => (text.StartsWith("flux:", StringComparison.Ordinal) ? text[5..] : text).Replace(".", "", StringComparison.Ordinal);
        return headed.Any(heading => heading == name
            || Undotted(heading) == Undotted(name)
            || (heading.EndsWith(".*", StringComparison.Ordinal) && (name == heading[..^2] || name.StartsWith(heading[..^1], StringComparison.Ordinal))));
    }

    // The parts a page's examples write that no Reference heads, each with the attributes written on it, and
    // the items a page lists as available for one of its components (the editor's toolbar items, each
    // `flux:editor.<item>`). Props and values are sorted, so moving an example on the page moves nothing here.
    private static IEnumerable<JsonObject> Unlisted(string markdown, string slug, IReadOnlyList<string> headed)
    {
        var found = new List<(string Name, List<(string Prop, List<string> Values)> Props)>();
        List<(string Prop, List<string> Values)> Part(string name)
        {
            var at = found.FindIndex(f => f.Name == name);
            if (at < 0)
            {
                found.Add((name, []));
                at = found.Count - 1;
            }

            return found[at].Props;
        }

        foreach (Match block in CodeBlock().Matches(markdown))
        {
            foreach (Match tag in FluxTag().Matches(block.Groups[1].Value))
            {
                var name = tag.Groups[1].Value;
                if (IsHeaded($"flux:{name}", headed))
                {
                    continue;
                }

                var props = Part($"flux:{name}");
                foreach (Match attribute in Attribute().Matches(tag.Groups[2].Value))
                {
                    var written = attribute.Groups[1].Value;
                    // A class is the app's, a directive is Livewire's or Alpine's: neither is a prop of the part.
                    if (Directive().IsMatch(written))
                    {
                        continue;
                    }

                    // `:prop="$expression"` binds a PHP value: the prop is documented, the expression is no value of it.
                    var bound = written.StartsWith(':');
                    var prop = bound ? written[1..] : written;
                    var at = props.FindIndex(p => p.Prop == prop);
                    if (at < 0)
                    {
                        props.Add((prop, []));
                        at = props.Count - 1;
                    }

                    var value = attribute.Groups[2].Success ? attribute.Groups[2].Value : attribute.Groups[3].Success ? attribute.Groups[3].Value : null;
                    if (!bound && !string.IsNullOrEmpty(value) && !props[at].Values.Contains(value))
                    {
                        props[at].Values.Add(value);
                    }
                }
            }
        }

        // "The following toolbar items are available:", then one "- `name`" line per item.
        var listing = false;
        foreach (var line in markdown.Split('\n'))
        {
            var item = ItemLine().Match(line);
            if (ItemsAvailable().IsMatch(line))
            {
                listing = true;
            }
            else if (listing && item.Success)
            {
                if (!IsHeaded($"flux:{slug}.{item.Groups[1].Value}", headed))
                {
                    Part($"flux:{slug}.{item.Groups[1].Value}");
                }
            }
            else if (line.Trim().Length > 0)
            {
                listing = false;
            }
        }

        static int ByName(string a, string b) => English.Compare(a, b, CompareOptions.None);
        return found.Select(f => new JsonObject
        {
            ["name"] = f.Name,
            ["examples"] = new JsonArray([.. f.Props.OrderBy(p => p.Prop, Comparer<string>.Create(ByName)).Select(p => (JsonNode?)(p.Values.Count > 0
                ? new JsonObject { ["name"] = p.Prop, ["values"] = new JsonArray([.. p.Values.OrderBy(v => v, Comparer<string>.Create(ByName)).Select(v => (JsonNode?)v)]) }
                : new JsonObject { ["name"] = p.Prop }))]),
        });
    }

    [GeneratedRegex(@"\((?:https://fluxui\.dev)/((components|layouts)/([a-z0-9-]+)\.md)\)")]
    private static partial Regex IndexEntry();

    [GeneratedRegex("^# (.+)$", RegexOptions.Multiline)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^## Reference\s*$", RegexOptions.Multiline)]
    private static partial Regex ReferenceHeading();

    [GeneratedRegex("^### (.+)$")]
    private static partial Regex PartHeading();

    [GeneratedRegex(@"^\*\*([A-Za-z ]+):\*\*\s*$")]
    private static partial Regex KindHeading();

    [GeneratedRegex("^- `([^`]+)`(?: - (.*))?$")]
    private static partial Regex EntryLine();

    [GeneratedRegex(@"Options?: (.*?)(?:\. |\.$|$)")]
    private static partial Regex OptionsSentence();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Ticked();

    [GeneratedRegex(@"`([^`]+)` \(default\)")]
    private static partial Regex TaggedDefault();

    [GeneratedRegex("Default: `([^`]+)`")]
    private static partial Regex StatedDefault();

    [GeneratedRegex("If `(?:true|false)`")]
    private static partial Regex IfBoolean();

    [GeneratedRegex(@"^```[^\n]*\n([\s\S]*?)^```", RegexOptions.Multiline)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"<flux:([a-z0-9.-]+)((?:\s+[^\s=>""'/]+(?:=(?:""[^""]*""|'[^']*'))?)*)\s*/?>")]
    private static partial Regex FluxTag();

    [GeneratedRegex(@"([^\s=>""'/]+)(?:=(?:""([^""]*)""|'([^']*)'))?")]
    private static partial Regex Attribute();

    [GeneratedRegex("^(class$|wire:|x-|@)")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^- `([a-z0-9-]+)`\s*$")]
    private static partial Regex ItemLine();

    [GeneratedRegex(@"items are available:\s*$")]
    private static partial Regex ItemsAvailable();
}
