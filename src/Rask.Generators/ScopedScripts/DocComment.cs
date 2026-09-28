using System;
using System.Collections.Generic;
using System.Text;

namespace Rask.Generators.ScopedScripts;

internal static class DocComment
{
    /// <summary>The JSDoc text of a declaration, split into its summary, <c>@param</c>s and <c>@returns</c>.</summary>
    public static (string Summary, Dictionary<string, string> Params, string? Returns) Split(string? doc)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(doc))
        {
            return (string.Empty, parameters, null);
        }

        var summary = new StringBuilder();
        string? returns = null;
        var section = new Section(summary, null, false);

        foreach (var rawLine in doc!.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("*", StringComparison.Ordinal))
            {
                line = line.Substring(1).Trim();
            }

            if (line.StartsWith("@", StringComparison.Ordinal))
            {
                Flush(section, parameters, ref returns);
                section = OpenTag(line);
                continue;
            }

            if (section.Text is not { } current)
            {
                continue;
            }

            if (current.Length > 0 && line.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
        }

        Flush(section, parameters, ref returns);
        return (summary.ToString().Trim(), parameters, returns);
    }

    private static void Flush(Section section, Dictionary<string, string> parameters, ref string? returns)
    {
        if (section.Text is null)
        {
            return;
        }

        if (section.Param is not null)
        {
            parameters[section.Param] = section.Text.ToString().Trim();
        }
        else if (section.IsReturns)
        {
            returns = section.Text.ToString().Trim();
        }
    }

    // The section an `@` line starts: a `@param`, the `@returns`, or — for any other tag — nothing collected.
    private static Section OpenTag(string line)
    {
        if (line.StartsWith("@param ", StringComparison.Ordinal))
        {
            var restOfLine = line.Substring("@param ".Length).Trim();
            if (restOfLine.StartsWith("{", StringComparison.Ordinal))
            {
                var close = restOfLine.IndexOf('}');
                restOfLine = close < 0 ? string.Empty : restOfLine.Substring(close + 1).Trim();
            }

            var space = restOfLine.IndexOf(' ');
            var param = space < 0 ? restOfLine : restOfLine.Substring(0, space);
            return new Section(
                new StringBuilder(space < 0 ? string.Empty : restOfLine.Substring(space + 1).TrimStart('-', ' ')),
                param,
                false);
        }

        if (line.StartsWith("@returns", StringComparison.Ordinal) || line.StartsWith("@return ", StringComparison.Ordinal))
        {
            var space = line.IndexOf(' ');
            return new Section(new StringBuilder(space < 0 ? string.Empty : line.Substring(space + 1)), null, true);
        }

        return new Section(null, null, false);
    }

    // What the lines being read belong to: the summary, one @param, the @returns, or nothing (Text null).
    private readonly record struct Section(StringBuilder? Text, string? Param, bool IsReturns);
}
