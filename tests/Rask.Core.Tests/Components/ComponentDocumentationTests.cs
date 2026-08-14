using System.Text.RegularExpressions;

namespace Rask.Core.Tests.Components;

/// <summary>
///     Every element component is a piece of public API a user hovers before they use it, and the
///     documentation ships in the package's XML file. These guards keep that surface from rotting: a new
///     tag added without a summary, or with an MDN link copied from the tag beside it, fails here rather
///     than shipping a wrong reference in an IDE tooltip.
///
///     <para>
///         The MDN paths are pinned deliberately. MDN moved its references under <c>/Reference/</c> —
///         HTML to <c>Web/HTML/Reference/Elements/{tag}</c> and SVG to <c>Web/SVG/Reference/Element/{tag}</c>,
///         plural for one and singular for the other — so the pre-move paths now redirect. Asserting the
///         current shape stops the old one creeping back in by copy-paste.
///     </para>
/// </summary>
public class ComponentDocumentationTests
{
    private const string HtmlBase = "https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Elements/";
    private const string SvgBase = "https://developer.mozilla.org/en-US/docs/Web/SVG/Reference/Element/";

    // Tags whose MDN page is not named after the tag: the six headings share one page.
    private static readonly Dictionary<string, string> PageOverrides = new(StringComparer.Ordinal)
    {
        ["h1"] = "Heading_Elements",
        ["h2"] = "Heading_Elements",
        ["h3"] = "Heading_Elements",
        ["h4"] = "Heading_Elements",
        ["h5"] = "Heading_Elements",
        ["h6"] = "Heading_Elements",
    };

    public static TheoryData<string, string, string> TagComponents()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (file, source) in ComponentSources())
        {
            var tag = Regex.Match(source, "TagName => \"([^\"]+)\"");
            if (!tag.Success)
            {
                continue;
            }

            var isSvg = file.Contains($"{Path.DirectorySeparatorChar}Svg{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            data.Add(Path.GetFileName(file), tag.Groups[1].Value, isSvg ? "svg" : "html");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TagComponents))]
    public void Every_tag_component_documents_itself_and_links_its_own_MDN_page(
        string fileName, string tag, string kind)
    {
        var source = ComponentSources().Single(s => Path.GetFileName(s.File) == fileName).Source;
        var doc = ClassDocComment(source);

        Assert.True(
            doc.Contains("<summary>", StringComparison.Ordinal),
            $"{fileName} renders <{tag}> but has no XML <summary>. It ships in the package's doc file, so "
            + "an undocumented component is a blank tooltip at every call site.");

        var expected = (kind == "svg" ? SvgBase : HtmlBase)
                       + (PageOverrides.TryGetValue(tag, out var page) ? page : tag);

        Assert.True(
            doc.Contains(expected, StringComparison.Ordinal),
            $"{fileName} renders <{tag}> so it must reference {expected}. A reference to a DIFFERENT "
            + "element is worse than none — it sends the reader to the wrong page with full confidence.");
    }

    [Fact]
    public void No_component_uses_the_pre_move_MDN_paths()
    {
        // The paths MDN redirects from. A redirect works today and is a dead link the day it stops.
        string[] stale = ["/docs/Web/HTML/Element/", "/docs/Web/SVG/Element/"];

        var offenders = ComponentSources()
            .Where(s => stale.Any(p => s.Source.Contains(p, StringComparison.Ordinal)))
            .Select(s => Path.GetFileName(s.File))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These components link MDN's pre-move paths, which now redirect. Use "
            + $"{HtmlBase}{{tag}} or {SvgBase}{{tag}}: {string.Join(", ", offenders)}");
    }

    // The doc comment attached to the type declaration: the run of `///` lines immediately above the
    // `public ... class X` line, skipping any attributes between them.
    private static string ClassDocComment(string source)
    {
        var lines = source.Split('\n');
        var declaration = Array.FindIndex(
            lines,
            l => Regex.IsMatch(l, @"^public (?:sealed |abstract |static )*(?:partial )?class \w+"));
        if (declaration < 0)
        {
            return string.Empty;
        }

        var i = declaration - 1;
        while (i >= 0 && lines[i].TrimStart().StartsWith('['))
        {
            i--;
        }

        var doc = new List<string>();
        while (i >= 0 && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal))
        {
            doc.Insert(0, lines[i]);
            i--;
        }

        return string.Join("\n", doc);
    }

    // Read once: every theory case looks the sources up, and there are ~180 of each.
    private static readonly Lazy<IReadOnlyList<(string File, string Source)>> Sources = new(() =>
    {
        var dir = Path.Combine(RepoRoot(), "src", "Rask.Core", "Components");
        return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => (f, File.ReadAllText(f)))
            .ToList();
    });

    private static IReadOnlyList<(string File, string Source)> ComponentSources() => Sources.Value;

    private static string RepoRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
