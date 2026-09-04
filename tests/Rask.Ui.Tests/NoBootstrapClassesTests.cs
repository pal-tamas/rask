using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     `Rask.Bootstrap` is gone and styling is Tailwind, compiled from each project's own source. A
///     Bootstrap class name left in the tree is therefore not a style — it is a string that reads as
///     though it styles something and does not.
/// </summary>
/// <remarks>
///     <para>
///         That is a bug class this repository has already paid for twice: deleting the package left
///         ~2,300 of these behind, and the ones on a <c>&lt;table&gt;</c> row survived a whole conversion
///         because a class name compiles no matter what it says. This test is what stops the next one
///         coming back.
///     </para>
///     <para>
///         It lives in the KIT's test project rather than Rask.Core's, and that is not filing. It needs
///         the kit's stylesheet to know which names are real, and referencing Rask.Ui from Rask.Core.Tests
///         changed what the chain generator injects into that compilation — enough to move a pinned
///         allocation measurement by 79 bytes. A convention test is not worth perturbing a performance
///         pin, so the test moved to where the reference already exists.
///     </para>
///     <para>
///         It scans CLASS POSITIONS only — <c>.Class("…")</c>, <c>Class:</c>, <c>Class =</c>,
///         <c>class="…"</c>, and Blazor's <c>AddAttribute(n, "class", "…")</c> in the comparison
///         benchmarks. Prose that discusses Bootstrap's own CSS (the changelog describes what the
///         deleted package did) is documentation of a third-party framework, not a usage, and is not
///         the subject here.
///     </para>
/// </remarks>
public sealed class NoBootstrapClassesTests
{
    /// <summary>
    ///     Class names Bootstrap 5 owns that Tailwind does NOT define with the same meaning.
    /// </summary>
    /// <remarks>
    ///     The spellings the two frameworks SHARE are deliberately absent — <c>border</c>,
    ///     <c>rounded</c>, <c>shadow-sm</c>, <c>bg-white</c>, <c>text-center</c>, <c>col-span-*</c>,
    ///     <c>gap-*</c>, <c>mb-*</c>, <c>me-*</c>, <c>p-*</c>, <c>w-full</c> and <c>col-auto</c> are all
    ///     live Tailwind utilities. Listing one of those would make this test fail on correct code,
    ///     which is the fastest way to get a convention test switched off.
    /// </remarks>
    private static readonly Regex Bootstrap = new(
        """
        ^(
            btn|btn-[a-z]+|btn-outline-[a-z]+|btn-group|btn-close|
            card|card-(body|header|footer|title|subtitle|text|link|img-top)|
            alert|alert-[a-z]+|alert-dismissible|badge|
            navbar|navbar-[a-z-]+|nav-(link|tabs|pills|item|fill|justified)|
            list-group|list-group-[a-z-]+|
            table-[a-z-]+|
            form-(control|control-sm|control-lg|select|select-sm|label|text|check|check-input|
                  check-label|check-inline|switch|floating|range|control-plaintext)|
            input-group|input-group-[a-z]+|
            collapse|collapsing|accordion[a-z-]*|offcanvas[a-z-]*|modal-[a-z]+|toast[a-z-]*|
            dropdown[a-z-]*|spinner-[a-z-]+|progress-bar|
            container|container-fluid|row|
            col-(sm|md|lg|xl|xxl)-[0-9]+|
            g-[0-9]|gx-[0-9]|gy-[0-9]|
            d-[a-z]+|d-(sm|md|lg|xl)-[a-z]+|
            justify-content-[a-z]+|align-items-[a-z]+|align-self-[a-z]+|
            text-(muted|dark|light|start|end|white-50)|text-body[a-z-]*|text-bg-[a-z]+|
            text-decoration-[a-z]+|
            fw-[a-z]+|fst-[a-z]+|fs-[1-6]|lh-[a-z0-9]+|lead|display-[1-6]|
            bg-body[a-z-]*|[a-z]+-subtle|
            border-(top|bottom|start|end)|rounded-(pill|circle)|
            w-(25|50|75|100)|h-(25|50|75|100)|mw-100|vh-100|
            visually-hidden[a-z-]*|stretched-link|sticky-(top|bottom)|fixed-(top|bottom)|
            float-(start|end)|clearfix|ratio-[0-9x]+|
            figure-caption|blockquote-footer|hstack|vstack|
            is-invalid|is-valid|invalid-feedback|valid-feedback|
            bi|bi-[a-z0-9-]+
        )$
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

    // A plain escaped string rather than a raw one: the pattern both contains and ends with a double
    // quote, which is exactly the shape raw-string delimiters cannot express without counting quotes.
    private static readonly Regex ClassPosition = new(
        "(?:\\.Class\\(|\\.ActiveClass\\(|\\bClass:\\s*|\\bClass\\s*=\\s*|class=\\\\?\"" +
        "|AddAttribute\\(\\d+,\\s*\"class\",\\s*)\\$?\"([^\"]*)\"",
        RegexOptions.Compiled);

    // `text-muted` is Bootstrap's, but Tailwind also generates it wherever a project's @theme declares
    // `--color-muted` — the landing site does exactly that. Keyed by the file so the exemption cannot
    // quietly spread to a project that has no such token.
    private static readonly HashSet<string> ThemeGenerated =
        new(StringComparer.Ordinal) { "samples/Rask.Example.Site/App.cs:text-muted" };

    /// <summary>
    ///     The class names daisyUI actually defines, read from the stylesheet the kit ships.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This test's premise is that a Bootstrap class name in the tree STYLES NOTHING. That was true
    ///         while Tailwind was the only styling system; it stopped being true when the kit took daisyUI,
    ///         because the two libraries share a great deal of vocabulary — <c>btn</c>, <c>card</c>,
    ///         <c>alert</c>, <c>badge</c>, <c>navbar</c>, <c>tabs</c>, <c>modal-*</c>, <c>dropdown-*</c>,
    ///         <c>toast</c> and more are live daisyUI classes. Reporting those would fail the test on
    ///         correct code, which the note above calls the fastest way to get a convention test switched
    ///         off, and is exactly why the spellings Tailwind shares were left out of the pattern already.
    ///     </para>
    ///     <para>
    ///         Read from the SHIPPED sheet rather than listed by hand, so it cannot drift as daisyUI grows
    ///         or shrinks. The cost is honest and worth stating: on a surface that does not inline the
    ///         kit's stylesheet, a genuine Bootstrap leftover spelled like a daisyUI class now goes
    ///         unreported. The alternative was a list that silently rots.
    ///     </para>
    /// </remarks>
    private static readonly HashSet<string> DaisyUi = DefinedClassNames(UiStylesheet.Css);

    private static HashSet<string> DefinedClassNames(string css)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(css, @"\.(-?[_a-zA-Z][\w-]*)"))
        {
            names.Add(m.Groups[1].Value);
        }

        return names;
    }

    [Fact]
    public void No_source_file_uses_a_Bootstrap_class_name()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var file in TrackedFiles(root))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match m in ClassPosition.Matches(text))
            {
                foreach (var token in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Bootstrap.IsMatch(token)
                        || DaisyUi.Contains(token)
                        || ThemeGenerated.Contains($"{relative}:{token}"))
                    {
                        continue;
                    }

                    var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{relative}:{line}: {token}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Bootstrap class names are still in class position. Rask.Bootstrap is deleted and styling is "
            + "Tailwind, so these style nothing:\n  " + string.Join("\n  ", offenders.Take(40)));
    }

    private static IEnumerable<string> TrackedFiles(string root)
    {
        string[] extensions = [".cs", ".md", ".txt", ".html", ".css", ".ts"];

        // Excluded RELATIVE to the repo root, never on the absolute path: this repo's own worktrees live
        // under .claude/worktrees, so an absolute "contains /worktrees/" check matches every file when
        // the tests run FROM a worktree — and the scan would pass by finding nothing.
        string[] skip =
        [
            "bin/",        // build output
            "obj/",
            ".git/",       // packed objects
            "node_modules/",
            "wwwroot/lib/", // vendored third-party JS/CSS
            ".claude/worktrees/", // other branches
        ];

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f =>
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                return !skip.Any(s => rel.StartsWith(s, StringComparison.Ordinal)
                                      || rel.Contains($"/{s}", StringComparison.Ordinal));
            });
    }

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
