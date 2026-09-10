using System.Text.RegularExpressions;

namespace Rask.Site.Tests.Infrastructure;

/// <summary>
///     Reading the repository's own stylesheets, for the tests that assert on what they declare.
/// </summary>
/// <remarks>
///     <para>
///     Shared because both readers here have already been got wrong once, in ways that are silent. The
///     showcase's <c>app.css</c> opens with a comment explaining why a <c>@theme</c> block cannot be
///     reached through an <c>@import</c> — and that comment contains the text <c>@theme { … }</c>, so a
///     reader that searches for the at-rule finds the prose first and parses a block with no declarations
///     in it. Every assertion downstream then passes on an empty dictionary.
///     </para>
///     <para>
///     The second is scope. <c>ui.css</c> declares <c>--color-ui-*</c> twice on purpose: once in
///     <c>@theme</c>, as the formula every palette starts from, and again in <c>@layer rask</c> as the
///     per-theme corrections for the six palettes that cannot afford it. A "last declaration wins" reader
///     over the whole file returns valentine's numbers as if they were the defaults.
///     </para>
/// </remarks>
internal static partial class Stylesheets
{
    /// <summary>The body of a file's <c>@theme</c> block, with comments removed.</summary>
    public static string ThemeBlock(string path)
    {
        var css = StripComments(File.ReadAllText(path));
        var start = css.IndexOf("@theme", StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"{path} declares no @theme block.");
        }

        var open = css.IndexOf('{', start);
        var depth = 0;

        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
            {
                depth++;
            }
            else if (css[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return css[(open + 1)..i];
                }
            }
        }

        throw new InvalidOperationException($"{path}'s @theme block is never closed.");
    }

    /// <summary>The <c>--custom-property: value</c> pairs in a block. Last declaration wins.</summary>
    public static Dictionary<string, string> Declarations(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match m in Declaration().Matches(StripComments(body)))
        {
            result[m.Groups["name"].Value] = m.Groups["value"].Value.Trim();
        }

        return result;
    }

    public static string StripComments(string css) => CssComment().Replace(css, " ");

    /// <summary>The repository root — the directory holding <c>Rask.slnx</c>.</summary>
    public static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("could not find the repository root.");
    }

    [GeneratedRegex(@"(?<name>--[a-z0-9-]+)\s*:\s*(?<value>[^;{}]+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();
}
