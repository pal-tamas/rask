using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     A daisyUI component reaches the kit's class list only because a kit component writes it.
/// </summary>
/// <remarks>
///     Tailwind reads every word of a scanned file as a candidate, comments included, and daisyUI emits a
///     component wherever its name is seen. So one bare word in a doc comment — "a magnifying glass" — put
///     <c>.glass</c> in the kit's sheet, in the class list compiled from it, and from there in every app's
///     stylesheet. Nothing failed until the slowest job in CI built a scaffolded app and found it.
/// </remarks>
public sealed partial class DaisyComponentsInTheClassListTests
{
    private static readonly string _kit = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui");

    // Already in the list from a comment or an identifier when this guard was written. Each goes when its
    // word leaves the kit, or when the kit scans only what its components write; none may be added.
    private static readonly string[] _knownFromProse =
        ["badge", "calendar", "card", "collapse", "link", "skeleton", "stat", "toast", "typography"];

    [Fact]
    public void Every_daisy_component_in_the_class_list_is_written_by_a_kit_component()
    {
        var written = WrittenInStringLiterals();
        var components = DaisyComponents();

        // A component the plugin is told to leave out has no rules in the sheet: what is left of its classes in
        // the list is another component's selector (daisyUI's menu styles a `.dropdown` inside it).
        var excluded = ExcludedFromThePlugin();
        var fromProse = components
            .Where(c => !excluded.Contains(c.Name) && c.Classes.Overlaps(KitConsumer.Classes) && !c.Classes.Overlaps(written))
            .ToList();

        Assert.True(
            fromProse.Select(c => c.Name).Order(StringComparer.Ordinal).SequenceEqual(_knownFromProse),
            "daisyUI components in rask-ui.classes.txt that no string literal in src/Rask.Ui writes: "
            + string.Join("; ", fromProse.Select(c => $"{c.Name} (.{string.Join(", .", c.Classes.Intersect(KitConsumer.Classes).Order(StringComparer.Ordinal))})"))
            + $". Expected exactly: {string.Join(", ", _knownFromProse)}. A new one is a bare word in a comment or an identifier "
            + "that Tailwind scanned (`grep -rnw <class> src/Rask.Ui`): reword it, e.g. hyphenate it or name it "
            + "with a <see cref>. One that is gone comes off the list in this test.");
    }

    [Fact]
    public void The_bundle_still_names_its_components_the_way_this_reads_them()
    {
        var components = DaisyComponents();

        var names = components.Select(c => c.Name).ToList();

        Assert.Contains("button", names);
        Assert.Contains("glass", names);
        Assert.Contains("btn", components.Single(c => c.Name == "button").Classes);
        Assert.Contains("btn", WrittenInStringLiterals());
    }

    // The bundle keeps each component's rules under a comment naming the file they came from.
    private static List<(string Name, HashSet<string> Classes)> DaisyComponents() =>
        [.. ObjectFile().Split(File.ReadAllText(Path.Combine(_kit, "Styles", "daisyui.mjs")))
            .Select(section => (Header: ObjectHeader().Match(section), Text: section))
            .Where(s => s.Header.Success)
            .Select(s => (
                s.Header.Groups[1].Value,
                ClassSelector().Matches(s.Text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal)))];

    // The `exclude: a, b;` line of the kit's `@plugin "./daisyui.mjs"` block.
    private static HashSet<string> ExcludedFromThePlugin() =>
        [.. PluginExclude().Match(File.ReadAllText(Path.Combine(_kit, "Styles", "ui.css"))).Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    // What a component can put in a class attribute: the words of the string literals in the kit's code,
    // each also without its variants (`sm:btn-wide` writes `btn-wide`).
    private static HashSet<string> WrittenInStringLiterals()
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in KitSources().SelectMany(File.ReadLines).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)))
        {
            foreach (var word in StringLiteral().Matches(line).SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            {
                words.Add(word);
                words.Add(word[(word.LastIndexOf(':') + 1)..]);
            }
        }

        return words;
    }

    private static IEnumerable<string> KitSources() =>
        Directory.EnumerateFiles(_kit, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(_kit, path).Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"));

    [GeneratedRegex(@"^// packages/daisyui/", RegexOptions.Multiline)]
    private static partial Regex ObjectFile();

    [GeneratedRegex(@"\A(?:components|utilities)/(\w+)/object\.js\n")]
    private static partial Regex ObjectHeader();

    [GeneratedRegex(@"^\s*exclude:([^;]*);", RegexOptions.Multiline)]
    private static partial Regex PluginExclude();

    [GeneratedRegex(@"\.(-?[A-Za-z_][\w-]*)")]
    private static partial Regex ClassSelector();

    [GeneratedRegex("""
        "((?:[^"\\]|\\.)*)"
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex StringLiteral();
}
