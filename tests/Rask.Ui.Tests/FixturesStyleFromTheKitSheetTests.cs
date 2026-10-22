using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     A test page that links only the kit's precompiled sheet gets exactly the classes that sheet defines.
/// </summary>
/// <remarks>
///     The browser suites draw their pages with <c>UiStylesheet.Css</c> and no Tailwind build, so a
///     class such a page writes is styled only if the kit's own scan produced it. Until #1214 the scan read
///     comments, and a chart's <c>aspect-3/1</c> was in the sheet because a doc comment suggested it;
///     when the scan stopped reading prose the chart had no height, nothing could be hovered, and the
///     journey timed out in CI. The sheet has to name what its callers rely on, and this is the check.
/// </remarks>
public sealed partial class FixturesStyleFromTheKitSheetTests
{
    // The kit frames: pages that link nothing but the kit's sheet. (The other apps in those projects bring
    // a Tailwind sheet of their own, or write names that are hooks.)
    private static readonly string _frames = Path.Combine("tests", "Rask.Server.E2E.Tests");

    // Written by a frame, never defined by the sheet, and nothing a journey measures: a header hidden from
    // the large breakpoint up, and a carousel slide's marker class.
    private static readonly string[] _unstyledByDesign = ["lg:hidden", "carousel-item"];

    [Fact]
    public void Every_class_a_kit_frame_writes_is_in_the_precompiled_sheet()
    {
        var defined = DefinedClasses(UiStylesheet.Css);

        var missing = Directory.EnumerateFiles(Path.Combine(RepoRoot.FullPath, _frames), "KitFramePages*.cs")
            .SelectMany(file => ClassCall().Matches(File.ReadAllText(file))
                .SelectMany(call => call.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(name => !defined.Contains(name) && !_unstyledByDesign.Contains(name))
                .Select(name => $"{name} ({Path.GetFileName(file)})"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "A kit frame writes a class the kit's precompiled sheet does not define, so it is unstyled there: "
            + string.Join(", ", missing)
            + ". Name it in ui.precompiled.css with `@source inline(...)` if a surface without Tailwind is meant to use it,"
            + " or give the page a style of its own.");
    }

    // `.Class("a b c")`: a literal only; a composed class is not this test's to read.
    [GeneratedRegex("""\.Class\("([^"{}$\\]+)"\)""")]
    private static partial Regex ClassCall();

    private static HashSet<string> DefinedClasses(string css) =>
        Regex.Matches(css, @"(?<![\w\\-])\.((?:\\.|[A-Za-z0-9_-])+)(?=[^{};]*\{)")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1"))
            .ToHashSet(StringComparer.Ordinal);
}
