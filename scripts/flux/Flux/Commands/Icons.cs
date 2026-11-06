using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rask.Flux.Commands;

/// <summary>
///     Generates Rask.Ui's icon set from Heroicons — the collection Flux UI's <c>flux:icon.*</c> is.
/// </summary>
/// <remarks>
///     Heroicons (https://github.com/tailwindlabs/heroicons) is MIT-licensed, Copyright (c) Tailwind Labs, Inc. The data
///     is npm's latest release, checked against the registry's integrity, read in memory, and never unpacked to disk. Out
///     of its four folders come src/Rask.Ui/Ui.IconName.cs (every icon, in PascalCase of Heroicons' name:
///     arrow-down-tray -> ArrowDownTray) and src/Rask.Ui/UiIconPaths.g.cs (the path data of all four variants). A release
///     that adds or drops an icon moves Ui.IconName, and the run records that in src/Rask.Ui/PublicAPI
///     (scripts/public-api/record.py, which builds the kit).
///     <code>
///     flux icons            # regenerate from the latest release
///     flux icons --check    # exit 1 when the committed files are not that release's
///     </code>
///     The upstream follower runs it every day.
/// </remarks>
[FluxCommand("icons", "Rask.Ui's icons from npm's latest heroicons")]
public sealed partial class Icons : IFluxCommand
{
    private const string Label = "flux icons";
    private const string Generator = "scripts/flux/Flux (flux icons)";

    private sealed record Variant(string Name, string Folder, Dictionary<string, string> Svg);

    // Folder in the package -> the Ui.IconVariant it draws, and the root attributes every file in it must
    // carry. UiIcon writes those attributes itself, so a release that changes one has to fail here.
    private static readonly Variant[] Variants =
    [
        new("Outline", "24/outline", new(StringComparer.Ordinal) { ["viewBox"] = "0 0 24 24", ["fill"] = "none", ["stroke-width"] = "1.5", ["stroke"] = "currentColor" }),
        new("Solid", "24/solid", new(StringComparer.Ordinal) { ["viewBox"] = "0 0 24 24", ["fill"] = "currentColor" }),
        new("Mini", "20/solid", new(StringComparer.Ordinal) { ["viewBox"] = "0 0 20 20", ["fill"] = "currentColor" }),
        new("Micro", "16/solid", new(StringComparer.Ordinal) { ["viewBox"] = "0 0 16 16", ["fill"] = "currentColor" }),
    ];

    // Names Heroicons deprecated in 2.1 and still ships at 24px and 20px only. They have no micro drawing and
    // are not on heroicons.com, so they are not icons here: every name left has all four variants.
    private static readonly string[] Deprecated =
    [
        "arrow-left-on-rectangle", "arrow-right-on-rectangle", "arrow-small-down", "arrow-small-left",
        "arrow-small-right", "arrow-small-up", "minus-small", "plus-small",
    ];

    /// <inheritdoc />
    public async Task<int> Run(string[] args)
    {
        var release = await Npm.Latest("heroicons", Label);
        var files = Npm.Untar(await Npm.Tarball(release, Label));
        var sets = Variants.Select(variant => (variant, icons: Read(files, variant))).ToList();
        var names = sets[0].icons.Keys.Where(name => !Deprecated.Contains(name)).Order(StringComparer.Ordinal).ToList();
        foreach (var (variant, icons) in sets)
        {
            var odd = names.Where(name => !icons.ContainsKey(name))
                .Concat(icons.Keys.Where(name => !names.Contains(name) && !Deprecated.Contains(name))).ToList();
            if (odd.Count > 0)
            {
                throw Npm.Fail(Label, $"{variant.Folder} does not hold the same icons as {sets[0].variant.Folder}: {string.Join(", ", odd)}");
            }
        }

        var outputs = new[]
        {
            (path: Repo.Path("src", "Rask.Ui", "Ui.IconName.cs"), text: EnumSource(release, names)),
            (path: Repo.Path("src", "Rask.Ui", "UiIconPaths.g.cs"), text: PathsSource(release, names, sets)),
        };

        var check = args.Contains("--check");
        var (stale, surface) = (0, false);
        foreach (var (path, text) in outputs)
        {
            var committed = File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
            if (committed == text)
            {
                continue;
            }

            stale++;
            // The enum is public: an icon added or dropped is surface PublicAPI must record.
            surface |= path.EndsWith("Ui.IconName.cs", StringComparison.Ordinal) && Members(committed) != Members(text);
            if (!check)
            {
                await File.WriteAllTextAsync(path, text);
            }
        }

        if (check && stale > 0)
        {
            throw Npm.Fail(Label, $"{stale} generated file(s) are stale — run: dotnet run --project scripts/flux/Flux -- icons");
        }

        if (!check && surface)
        {
            var record = new ProcessStartInfo("python3") { WorkingDirectory = Repo.Root, UseShellExecute = false };
            record.ArgumentList.Add(Repo.Path("scripts", "public-api", "record.py"));
            record.ArgumentList.Add("src/Rask.Ui");
            using var recorded = Process.Start(record)!;
            await recorded.WaitForExitAsync();
            if (recorded.ExitCode != 0)
            {
                throw Npm.Fail(Label, "the icons that moved could not be recorded in src/Rask.Ui/PublicAPI");
            }
        }

        var bytes = sets.Sum(set => Blob(names, set.icons).Length);
        Console.WriteLine($"flux icons: heroicons {release.Version} — {names.Count} icons x {sets.Count} variants, {bytes} bytes of path data.");
        return 0;
    }

    // One folder: icon name -> its shapes, each encoded. Anything UiIcon does not draw fails here.
    private static Dictionary<string, List<string>> Read(Dictionary<string, string> files, Variant variant)
    {
        var icons = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var prefix = $"package/{variant.Folder}/";
        foreach (var (path, svg) in files)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".svg", StringComparison.Ordinal))
            {
                continue;
            }

            var name = path[prefix.Length..^4];
            var root = OneSvg().Match(svg);
            if (!root.Success)
            {
                throw Npm.Fail(Label, $"{path}: not one <svg>");
            }

            var expected = new Dictionary<string, string>(StringComparer.Ordinal) { ["xmlns"] = "http://www.w3.org/2000/svg" };
            foreach (var (key, value) in variant.Svg)
            {
                expected[key] = value;
            }

            expected["aria-hidden"] = "true";
            expected["data-slot"] = "icon";
            if (!Same(Attributes(root.Groups[1].Value), expected))
            {
                throw Npm.Fail(Label, $"{path}: unexpected root attributes {root.Groups[1].Value}");
            }

            var paths = new List<string>();
            var rest = Shape().Replace(root.Groups[2].Value, m =>
            {
                paths.Add(m.Groups[1].Value == "rect" ? Rect(path, Attributes(m.Groups[2].Value)) : PathOf(path, variant, Attributes(m.Groups[2].Value)));
                return "";
            });
            if (rest.Trim().Length > 0 || paths.Count == 0)
            {
                throw Npm.Fail(Label, $"{path}: something other than <path> elements: {rest.Trim()}");
            }

            icons[name] = paths;
        }

        return icons.Count > 0 ? icons : throw Npm.Fail(Label, $"{variant.Folder}: no icons in the tarball");
    }

    // A <path>, with the one mark its attributes amount to: an outline path is stroked with round caps and
    // round joins, or — marked 'J' — with a round join alone; a solid path is filled by the non-zero rule or,
    // marked 'E', by the even-odd one.
    private static string PathOf(string file, Variant variant, Dictionary<string, string> attributes)
    {
        var others = attributes.Where(a => a.Key != "d").ToDictionary(StringComparer.Ordinal);
        attributes.TryGetValue("d", out var d);
        var marks = variant.Name == "Outline"
            ? new[] { ("", new Dictionary<string, string>(StringComparer.Ordinal) { ["stroke-linecap"] = "round", ["stroke-linejoin"] = "round" }), ("J", new Dictionary<string, string>(StringComparer.Ordinal) { ["stroke-linejoin"] = "round" }) }
            : [("", new Dictionary<string, string>(StringComparer.Ordinal)), ("E", new Dictionary<string, string>(StringComparer.Ordinal) { ["fill-rule"] = "evenodd", ["clip-rule"] = "evenodd" })];
        var mark = marks.Where(m => Same(others, m.Item2)).Select(m => m.Item1).FirstOrDefault();
        if (string.IsNullOrEmpty(d) || mark is null)
        {
            throw Npm.Fail(Label, $"{file}: unexpected <path> attributes {Js.Stringify(Js.ToNode(others))}");
        }

        if (!PathData().IsMatch(d) || ReservedMark().IsMatch(d))
        {
            throw Npm.Fail(Label, $"{file}: path data outside the encoding: {d}");
        }

        return mark + d;
    }

    // A <rect> ('R'), as "x y width height rx".
    private static string Rect(string file, Dictionary<string, string> attributes)
    {
        string[] keys = ["x", "y", "width", "height", "rx"];
        var numbers = keys.Select(k => attributes.GetValueOrDefault(k)).ToList();
        if (attributes.Keys.Any(k => !keys.Contains(k)) || numbers.Any(n => !Decimal().IsMatch(n ?? "")))
        {
            throw Npm.Fail(Label, $"{file}: unexpected <rect>");
        }

        return $"R{string.Join(' ', numbers)}";
    }

    private static Dictionary<string, string> Attributes(string raw)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AttributeValue().Matches(raw))
        {
            found[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return found;
    }

    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static string Pascal(string name) => string.Concat(name.Split('-').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    // One icon in a variant's blob: its shapes joined by '|'. Path data opens with M or m and holds no E, J
    // or R, so a mark cannot be mistaken for it.
    private static string Encode(List<string> shapes) => string.Join('|', shapes);

    private static string Blob(List<string> names, Dictionary<string, List<string>> icons) => string.Concat(names.Select(name => Encode(icons[name])));

    private static List<string> Header(Npm.Release release) =>
    [
        "// <auto-generated />",
        $"// Generated by {Generator} from heroicons {release.Version} ({release.Tarball},",
        $"// {release.Integrity}).",
        "// Heroicons is MIT-licensed, Copyright (c) Tailwind Labs, Inc. — https://github.com/tailwindlabs/heroicons",
        "// Do not edit: change the script and run it again.",
        "#nullable enable",
        "",
    ];

    private static string EnumSource(Npm.Release release, List<string> names)
    {
        var lines = Header(release);
        lines.AddRange(
        [
            "namespace Rask;",
            "",
            "public static partial class Ui",
            "{",
            "    /// <summary>",
            $"    ///     Every icon <see cref=\"UiIcon\" /> draws: the whole of <see href=\"https://heroicons.com\">Heroicons</see> {release.Version}",
            "    ///     under Heroicons' own names (<c>arrow-down-tray</c> is <see cref=\"ArrowDownTray\" />), plus Flux's",
            "    ///     <see cref=\"Loading\" /> spinner. Each comes in the four <see cref=\"IconVariant\" />s.",
            "    /// </summary>",
            "    public enum IconName",
            "    {",
        ]);
        foreach (var name in names)
        {
            lines.AddRange([$"        /// <summary>Heroicons' <c>{name}</c>.</summary>", $"        {Pascal(name)},", ""]);
        }

        lines.AddRange(
        [
            "        /// <summary>Flux's loading spinner (<c>flux:icon.loading</c>): not a Heroicon, and it spins.</summary>",
            "        Loading,",
            "    }",
            "}",
            "",
        ]);
        return string.Join('\n', lines);
    }

    private static string PathsSource(Npm.Release release, List<string> names, List<(Variant variant, Dictionary<string, List<string>> icons)> sets)
    {
        var lines = Header(release);
        lines.AddRange(
        [
            "namespace Rask;",
            "",
            "/// <summary>",
            "///     The path data behind every <see cref=\"Ui.IconName\" />, one table per <see cref=\"Ui.IconVariant\" />.",
            "/// </summary>",
            "/// <remarks>",
            "///     UTF-8 literals, so each table is bytes in the assembly's image rather than objects on the heap: nothing is",
            "///     allocated until an icon is drawn. An icon is <c>Data[Offsets[i]..Offsets[i + 1]]</c> with <c>i</c> its",
            "///     enum value; its shapes are separated by <c>|</c>. A shape is path data — led by <c>E</c> when it is filled",
            "///     by the even-odd rule, by <c>J</c> when its stroke has a round join and no round cap — or <c>R</c> and a",
            "///     rectangle's <c>x y width height rx</c>.",
            "/// </remarks>",
            "internal static partial class UiIconPaths",
            "{",
            "    /// <summary>How many icons each table holds: every <see cref=\"Ui.IconName\" /> before <see cref=\"Ui.IconName.Loading\" />.</summary>",
            $"    internal const int Count = {names.Count.ToString(CultureInfo.InvariantCulture)};",
        ]);
        foreach (var (variant, icons) in sets)
        {
            var encoded = names.Select(name => Encode(icons[name])).ToList();
            var offsets = new List<int> { 0 };
            foreach (var icon in encoded)
            {
                offsets.Add(offsets[^1] + icon.Length);
            }

            lines.AddRange(["", $"    /// <summary>Heroicons' <c>{variant.Folder}</c>.</summary>", $"    internal static ReadOnlySpan<byte> {variant.Name}Data =>"]);
            for (var i = 0; i < encoded.Count; i++)
            {
                lines.Add($"        \"{encoded[i]}\"u8{(i == encoded.Count - 1 ? ";" : " +")} // {names[i]}");
            }

            lines.AddRange(["", $"    /// <summary>Where each icon starts in <see cref=\"{variant.Name}Data\" />, and where the last one ends.</summary>",
                $"    internal static ReadOnlySpan<int> {variant.Name}Offsets =>", "    ["]);
            for (var i = 0; i < offsets.Count; i += 16)
            {
                lines.Add($"        {string.Join(", ", offsets.Skip(i).Take(16).Select(o => o.ToString(CultureInfo.InvariantCulture)))},");
            }

            lines.Add("    ];");
        }

        lines.AddRange(["}", ""]);
        return string.Join('\n', lines);
    }

    // The members of Ui.IconName as written: one `Name,` a line.
    private static string Members(string text) => string.Join(' ', MemberLine().Matches(text).Select(m => m.Value));

    [GeneratedRegex(@"^<svg([^>]*)>(.*)</svg>\s*$", RegexOptions.Singleline)]
    private static partial Regex OneSvg();

    [GeneratedRegex("<(path|rect)([^>]*?)/>")]
    private static partial Regex Shape();

    [GeneratedRegex(@"^[Mm][-0-9.\sA-Za-z]*$")]
    private static partial Regex PathData();

    [GeneratedRegex("[EJR|]")]
    private static partial Regex ReservedMark();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)?$")]
    private static partial Regex Decimal();

    [GeneratedRegex(@"([\w:-]+)=""([^""]*)""")]
    private static partial Regex AttributeValue();

    [GeneratedRegex(@"^ {8}\w+,$", RegexOptions.Multiline)]
    private static partial Regex MemberLine();

}
