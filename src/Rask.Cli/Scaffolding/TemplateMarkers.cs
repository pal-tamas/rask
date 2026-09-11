using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Cli.Scaffolding;

/// <summary>
///     Strips the battery conditionals out of a committed template file.
/// </summary>
/// <remarks>
///     <para>
///         A region is delimited by a COMMENT in the file's own language, so every template file stays
///         valid as it sits in the tree — it compiles, it runs, and a reader sees the default app rather
///         than a soup of substitution syntax. That property is what lets the template trees replace the
///         sample apps: they are real apps.
///     </para>
///     <code>
///     // rask:if data          …   // rask:end          C#, TypeScript, JavaScript
///     &lt;!-- rask:if data --&gt;  …   &lt;!-- rask:end --&gt;   csproj, HTML, Vue, Svelte
///     #  rask:if data          …   #  rask:end          Dockerfile, .dockerignore, .editorconfig
///     /* rask:if data */       …   /* rask:end */       CSS
///     </code>
///     <para>
///         A condition is a CONJUNCTION — <c>rask:if wasm cqrs</c> keeps its lines only when both are on.
///         That is not generality for its own sake: <c>Browser/BrowserStartup.cs</c> is absent without
///         <c>--wasm</c> and absent without <c>--cqrs</c>, and both wasm-on/cqrs-off and wasm-off/cqrs-on
///         are reachable, so no single flag describes it. <c>rask:ifnot</c> is the negation, for the
///         handful of places where turning a battery off SUBSTITUTES text rather than removing it (the
///         home page's nav links to the docs when there are no accounts to sign into).
///     </para>
///     <para>
///         JSON carries no comments and therefore no markers. Nothing needs them: of the fifteen trees,
///         not one has a conditional JSON file, and if one ever does the condition belongs in
///         <c>template.json</c> where it can be expressed as data.
///     </para>
/// </remarks>
internal static partial class TemplateMarkers
{
    [GeneratedRegex(
        @"^[ \t]*(?://|<!--|/\*|#|@\*)[ \t]*rask:(?<kind>if|ifnot|end)(?:[ \t]+(?<flags>[A-Za-z0-9_ -]+?))?[ \t]*(?:-->|\*/|\*@)?[ \t]*$",
        RegexOptions.ExplicitCapture)]
    private static partial Regex Marker { get; }

    /// <summary>Whether <paramref name="line"/> is a marker rather than content.</summary>
    public static bool IsMarker(string line) => Marker.IsMatch(line);

    /// <summary>
    ///     <paramref name="content"/> with every unsatisfied region removed and every marker line
    ///     dropped.
    /// </summary>
    /// <param name="content">The template file, verbatim.</param>
    /// <param name="on">The flags that are on.</param>
    /// <param name="path">Only for the exception message when a file's markers do not balance.</param>
    public static string Apply(string content, IReadOnlySet<string> on, string path)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(on);

        // Nothing to do for the great majority of files, and this avoids rebuilding them line by line.
        if (!content.Contains("rask:", StringComparison.Ordinal))
        {
            return content;
        }

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Split('\n');
        var kept = new StringBuilder(content.Length);
        var depth = 0;
        var suppressedAt = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // The split leaves a trailing empty entry for content that ends with a newline; it is the
            // terminator, not a line, and re-emitting it would add a blank line on every pass.
            if (i == lines.Length - 1 && line.Length == 0 && content.EndsWith('\n'))
            {
                break;
            }

            var match = Marker.Match(line);
            if (match.Success)
            {
                if (match.Groups["kind"].Value == "end")
                {
                    if (depth == 0)
                    {
                        throw new InvalidOperationException(
                            $"{path}: a rask:end with no matching rask:if.");
                    }

                    depth--;

                    // >= and not >: a region opened at depth 0 records suppressedAt 0, and closing it
                    // brings depth back to 0. With a strict comparison the suppression is never lifted
                    // and everything after the first unsatisfied region is silently dropped.
                    if (suppressedAt >= 0 && suppressedAt >= depth)
                    {
                        suppressedAt = -1;
                    }

                    continue;
                }

                var satisfied = match.Groups["flags"].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .All(on.Contains);

                if (match.Groups["kind"].Value == "ifnot")
                {
                    satisfied = !satisfied;
                }

                if (!satisfied && suppressedAt < 0)
                {
                    suppressedAt = depth;
                }

                depth++;
                continue;
            }

            if (suppressedAt < 0)
            {
                kept.Append(line).Append(newline);
            }
        }

        if (depth != 0)
        {
            throw new InvalidOperationException($"{path}: {depth} rask:if region(s) were never closed.");
        }

        return kept.ToString();
    }
}
