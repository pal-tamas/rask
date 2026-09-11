using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.External.Tasks;

/// <summary>One package island found in the source, before the compile.</summary>
/// <remarks>A class rather than a record: this assembly targets netstandard2.0, which has no init accessors.</remarks>
internal sealed class ScannedPackageIsland
{
    /// <param name="name">The class's simple name.</param>
    /// <param name="runtime">The runtime its base chain reaches.</param>
    /// <param name="module">The constant its <c>Module</c> override returns.</param>
    /// <param name="declaringFile">The file holding the override — the snapshot is written beside it.</param>
    /// <param name="line">The 1-based line of the override, for diagnostics.</param>
    public ScannedPackageIsland(string name, string runtime, string module, string declaringFile, int line)
    {
        Name = name;
        Runtime = runtime;
        Module = module;
        DeclaringFile = declaringFile;
        Line = line;
    }

    /// <summary>The class's simple name.</summary>
    public string Name { get; }

    /// <summary>The runtime its base chain reaches.</summary>
    public string Runtime { get; }

    /// <summary>The constant its <c>Module</c> override returns.</summary>
    public string Module { get; }

    /// <summary>The file holding the override.</summary>
    public string DeclaringFile { get; }

    /// <summary>The 1-based line of the override.</summary>
    public int Line { get; }

    /// <summary>Where this island's props snapshot lives.</summary>
    public string SnapshotPath =>
        Path.Combine(Path.GetDirectoryName(DeclaringFile) ?? string.Empty, Name + ".props.json");
}

/// <summary>
///     Finds the islands whose constant <c>Module</c> names a package, reading the C# source before it is
///     compiled.
/// </summary>
/// <remarks>
///     <para>
///         Before the compile for the same reason <see cref="ExternalSourceScan" /> is: the snapshot this
///         produces is an additional file the compile reads, so it has to exist before the compile that would
///         otherwise say which islands there are.
///     </para>
///     <para>
///         A real lexer rather than regular expressions over the raw text, because the thing being read is a
///         STRING. The comment-stripping regex the runtime scan uses reaches inside string literals, which is
///         harmless for a base list but cuts <c>"https://…"</c> or <c>"@scope/pkg//x"</c> in half here. So the
///         source is read twice-over: one view with comments and string CONTENTS blanked (structure — braces,
///         keywords — cannot be faked by text in either), and the original, from which the literal found at a
///         position in the first view is read back and unescaped.
///     </para>
///     <para>
///         An approximation of Roslyn, checked against Roslyn on the same build: after the compile, an island
///         the assembly declares with a package module that this scan did not find is reported
///         (RASKISLAND009), because its props were never extracted.
///     </para>
/// </remarks>
internal static class ExternalPackageScan
{
    // No base list required: a partial part that only overrides Module usually repeats none, and it is exactly
    // the part the snapshot has to be written beside. Which classes are islands at all comes from the runtime map.
    private static readonly Regex Declaration = new(
        @"\bclass\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ModuleOverride = new(
        @"\boverride\s+string\s+Module\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every package island in <paramref name="sources" />, using <paramref name="runtimes" /> — the islands
    ///     <see cref="ExternalSourceScan.IslandRuntimes" /> found — to say which classes are islands at all.
    /// </summary>
    public static IReadOnlyList<ScannedPackageIsland> PackageIslands(
        IEnumerable<string> sources,
        IReadOnlyDictionary<string, string> runtimes)
    {
        var found = new Dictionary<string, ScannedPackageIsland>(StringComparer.Ordinal);

        foreach (var path in sources)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (text.IndexOf("Module", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            foreach (var island in Scan(text, path, runtimes))
            {
                // A partial class spelled across files: the part that overrides Module is the one that counts.
                if (!found.ContainsKey(island.Name))
                {
                    found[island.Name] = island;
                }
            }
        }

        var result = new List<ScannedPackageIsland>(found.Values);
        result.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    /// <summary>The package islands one file declares. Exposed for tests.</summary>
    internal static IEnumerable<ScannedPackageIsland> Scan(
        string text,
        string path,
        IReadOnlyDictionary<string, string> runtimes)
    {
        var structure = Blank(text);

        foreach (Match declaration in Declaration.Matches(structure))
        {
            var name = declaration.Groups["name"].Value;
            if (!runtimes.TryGetValue(name, out var runtime))
            {
                continue;
            }

            // The body is the first brace after the name, unless a ';' ends the declaration first: a bodiless
            // `class Point(int X, int Y);` must not claim the next class's override as its own.
            var open = structure.IndexOfAny(['{', ';'], declaration.Index + declaration.Length);
            if (open < 0 || structure[open] != '{')
            {
                continue;
            }

            var close = MatchingBrace(structure, open);
            if (close < 0)
            {
                continue;
            }

            if (FindOverride(text, structure, open + 1, close) is { } module
                && ExternalPackageSpecifier.IsBare(module.Value))
            {
                yield return new ScannedPackageIsland(name, runtime, module.Value, path, LineOf(text, module.Position));
            }
        }
    }

    /// <summary>
    ///     The constant a <c>Module</c> override at the top level of a class body returns, in any of the four
    ///     forms the island generator reads: <c>=&gt; "…";</c>, <c>{ get =&gt; "…"; }</c>,
    ///     <c>{ get { return "…"; } }</c>, and <c>{ get; } = "…";</c>.
    /// </summary>
    private static (string Value, int Position)? FindOverride(string text, string structure, int start, int end)
    {
        var body = structure.Substring(start, end - start);
        foreach (Match match in ModuleOverride.Matches(body))
        {
            var at = start + match.Index;

            // Only at the class's own level: a nested class's override belongs to the nested class.
            if (Depth(structure, start, at) != 0)
            {
                continue;
            }

            var cursor = SkipSpace(structure, at + match.Length);
            string? literal = null;

            if (Starts(structure, cursor, "=>"))
            {
                literal = ReadLiteral(text, structure, SkipSpace(structure, cursor + 2));
            }
            else if (Starts(structure, cursor, "{"))
            {
                var inner = SkipSpace(structure, cursor + 1);
                if (Starts(structure, inner, "get"))
                {
                    var afterGet = SkipSpace(structure, inner + 3);
                    if (Starts(structure, afterGet, "=>"))
                    {
                        literal = ReadLiteral(text, structure, SkipSpace(structure, afterGet + 2));
                    }
                    else if (Starts(structure, afterGet, "{"))
                    {
                        var ret = SkipSpace(structure, afterGet + 1);
                        if (Starts(structure, ret, "return"))
                        {
                            literal = ReadLiteral(text, structure, SkipSpace(structure, ret + 6));
                        }
                    }
                    else if (Starts(structure, afterGet, ";"))
                    {
                        var closing = structure.IndexOf('}', afterGet);
                        var equals = closing < 0 ? -1 : SkipSpace(structure, closing + 1);
                        if (equals >= 0 && Starts(structure, equals, "="))
                        {
                            literal = ReadLiteral(text, structure, SkipSpace(structure, equals + 1));
                        }
                    }
                }
            }

            if (literal is not null)
            {
                return (literal, at);
            }
        }

        return null;
    }

    /// <summary>
    ///     A copy of <paramref name="text" /> of the same length with comments and the contents of string and
    ///     character literals replaced by spaces — quotes kept, line breaks kept — so structure can be matched
    ///     without anything inside a comment or a string faking it.
    /// </summary>
    internal static string Blank(string text)
    {
        var sb = new StringBuilder(text);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '/' && Next(text, i) == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    sb[i++] = ' ';
                }

                continue;
            }

            if (c == '/' && Next(text, i) == '*')
            {
                var endComment = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var stop = endComment < 0 ? text.Length : endComment + 2;
                for (; i < stop; i++)
                {
                    sb[i] = text[i] == '\n' ? '\n' : ' ';
                }

                continue;
            }

            if (c == '"' || ((c == '@' || c == '$') && StringStart(text, i) >= 0))
            {
                var open = c == '"' ? i : StringStart(text, i);
                var closeAt = StringEnd(text, open, IsVerbatim(text, i, open));
                for (var j = open + 1; j < closeAt && j < text.Length; j++)
                {
                    if (text[j] != '"' && text[j] != '\n')
                    {
                        sb[j] = ' ';
                    }
                }

                i = closeAt + 1;
                continue;
            }

            if (c == '\'')
            {
                var j = i + 1;
                while (j < text.Length && text[j] != '\'' && text[j] != '\n')
                {
                    if (text[j] == '\\')
                    {
                        sb[j] = ' ';
                        j++;
                    }

                    if (j < text.Length)
                    {
                        sb[j] = ' ';
                    }

                    j++;
                }

                i = j + 1;
                continue;
            }

            i++;
        }

        return sb.ToString();
    }

    /// <summary>The value of the string literal whose opening quote (or <c>@</c> / raw run) is at <paramref name="at" />.</summary>
    private static string? ReadLiteral(string text, string structure, int at)
    {
        if (at >= text.Length)
        {
            return null;
        }

        var verbatim = text[at] == '@';
        var open = verbatim ? at + 1 : at;
        if (open >= text.Length || text[open] != '"')
        {
            return null;
        }

        // Raw string literal: three or more quotes, contents taken verbatim up to the same run.
        var run = verbatim ? 1 : QuoteRun(text, open);
        if (run >= 3)
        {
            var closing = text.IndexOf(new string('"', run), open + run, StringComparison.Ordinal);
            if (closing < 0)
            {
                return null;
            }

            var raw = text.Substring(open + run, closing - open - run);
            return FollowedBySemicolon(structure, closing + run) ? raw.Trim('\r', '\n') : null;
        }

        var sb = new StringBuilder();
        var i = open + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                if (verbatim && Next(text, i) == '"')
                {
                    sb.Append('"');
                    i += 2;
                    continue;
                }

                return FollowedBySemicolon(structure, i + 1) ? sb.ToString() : null;
            }

            if (!verbatim && c == '\\' && i + 1 < text.Length)
            {
                var e = text[i + 1];
                sb.Append(e switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '0' => '\0',
                    _ => e,
                });
                i += 2;
                continue;
            }

            if (c == '\n')
            {
                return null;
            }

            sb.Append(c);
            i++;
        }

        return null;
    }

    /// <summary>Only a whole constant counts: <c>"a" + b</c> is computed, which is RASK059 for the generator.</summary>
    private static bool FollowedBySemicolon(string structure, int at)
    {
        var next = SkipSpace(structure, at);
        return next < structure.Length && structure[next] == ';';
    }

    private static int MatchingBrace(string structure, int open)
    {
        var depth = 0;
        for (var i = open; i < structure.Length; i++)
        {
            if (structure[i] == '{')
            {
                depth++;
            }
            else if (structure[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Depth(string structure, int start, int at)
    {
        var depth = 0;
        for (var i = start; i < at; i++)
        {
            if (structure[i] == '{')
            {
                depth++;
            }
            else if (structure[i] == '}')
            {
                depth--;
            }
        }

        return depth;
    }

    // `@"`, `$"`, `$@"`, `@$"`, and `$$"""` all open a string; returns the index of the first quote, or -1.
    private static int StringStart(string text, int i)
    {
        var j = i;
        while (j < text.Length && (text[j] == '@' || text[j] == '$'))
        {
            j++;
        }

        return j < text.Length && text[j] == '"' && j > i ? j : -1;
    }

    private static bool IsVerbatim(string text, int prefixStart, int quote) =>
        text.Substring(prefixStart, quote - prefixStart).IndexOf('@') >= 0;

    // The index of the closing quote of the string whose first quote is at `open`.
    private static int StringEnd(string text, int open, bool verbatim)
    {
        var run = verbatim ? 1 : QuoteRun(text, open);
        if (run >= 3)
        {
            var closing = text.IndexOf(new string('"', run), open + run, StringComparison.Ordinal);
            return closing < 0 ? text.Length - 1 : closing + run - 1;
        }

        var i = open + 1;
        while (i < text.Length)
        {
            if (text[i] == '"')
            {
                if (verbatim && Next(text, i) == '"')
                {
                    i += 2;
                    continue;
                }

                return i;
            }

            if (!verbatim && text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (!verbatim && text[i] == '\n')
            {
                return i;
            }

            i++;
        }

        return text.Length - 1;
    }

    private static int QuoteRun(string text, int at)
    {
        var run = 0;
        while (at + run < text.Length && text[at + run] == '"')
        {
            run++;
        }

        // Exactly two quotes is an empty literal, not the start of a raw one.
        return run == 2 ? 1 : run;
    }

    private static char Next(string text, int i) => i + 1 < text.Length ? text[i + 1] : '\0';

    private static int SkipSpace(string structure, int i)
    {
        while (i < structure.Length && char.IsWhiteSpace(structure[i]))
        {
            i++;
        }

        return i;
    }

    private static bool Starts(string structure, int at, string token) =>
        at + token.Length <= structure.Length
        && string.CompareOrdinal(structure, at, token, 0, token.Length) == 0
        && (token.Length == 1 || !char.IsLetter(token[0])
            || at + token.Length == structure.Length || !char.IsLetterOrDigit(structure[at + token.Length]));

    private static int LineOf(string text, int position)
    {
        var line = 1;
        for (var i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
