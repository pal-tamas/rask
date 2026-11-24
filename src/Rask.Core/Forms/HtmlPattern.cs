using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Rask.Core.Forms;

/// <summary>
///     A field's regular expression as the <c>pattern</c> of the input bound to it — only where the browser reads it the
///     same way. A browser compiles a <c>pattern</c> as a JavaScript expression (the <c>v</c> flag) that must match the whole
///     text; .NET reads its own dialect, so a construct the two read differently (a lookbehind, a named group, an inline
///     option, <c>\w</c>, <c>\b</c>, a class subtraction) gives no <c>pattern</c> at all, and the field is still checked
///     when the form is sent. Not an API to write against: what a generated value object and a validation library's
///     reader call.
/// </summary>
/// <remarks>
///     The rule the browser is told never refuses what the field's own check takes: it may only take more. So a counted
///     run of anything (<c>.{2,10}</c>) — which .NET counts in UTF-16 units and the browser in characters — is not told.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HtmlPattern
{
    private const string Anything = @"[\s\S]*";
    private const string Blanks = @"\s*";

    // One answer per pattern an app declares: they are constants in its code, so this stays as small as they are few.
    private static readonly ConcurrentDictionary<string, string?> Translated = new(StringComparer.Ordinal);

    /// <summary>
    ///     A pattern a match of which is FOUND somewhere in the text — what <c>Regex.IsMatch</c> asks, a value object's
    ///     <c>Text(Value, Pattern: …)</c> and FluentValidation's <c>Matches</c> — as one the WHOLE text must match, what
    ///     <see cref="FieldShape.Pattern" /> holds.
    /// </summary>
    /// <param name="pattern">The .NET regular expression.</param>
    /// <param name="trimmed">Whether the text is trimmed before it is matched, so blanks around it are taken as well.</param>
    /// <returns>The whole-text pattern, or null where none says the same.</returns>
    public static string? Found(string pattern, bool trimmed)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (HtmlPatternScanner.Scan(pattern) is not { } scanned)
        {
            return null;
        }

        var opens = scanned is { OpensAnchored: true, Alternates: false };
        var closes = scanned is { ClosesAnchored: true, Alternates: false };
        var core = pattern[(opens ? 1 : 0)..(pattern.Length - (closes ? 1 : 0))];

        // A trimmed text no longer starts or ends where the input's does: an inner anchor or a lookahead reads its edges.
        if (trimmed && (scanned.Anchors > (opens ? 1 : 0) + (closes ? 1 : 0) || scanned.LooksAround))
        {
            return null;
        }

        var left = Edge(opens, trimmed);
        var right = Edge(closes, trimmed);
        var body = scanned.Alternates && left.Length + right.Length > 0 ? "(?:" + core + ")" : core;
        return left + body + right;
    }

    // What stands for an edge of the text: anything before or after the match where it is not anchored, the blanks a
    // trim cut off where it is.
    private static string Edge(bool anchored, bool trimmed)
    {
        if (!anchored)
        {
            return Anything;
        }

        return trimmed ? Blanks : "";
    }

    /// <summary>
    ///     The <c>pattern</c> attribute that says what <paramref name="pattern" /> — a .NET expression the whole text must
    ///     match — says, or null where the browser would read it otherwise, or it is no valid expression.
    /// </summary>
    internal static string? From(string? pattern) =>
        pattern is { Length: > 0 } given ? Translated.GetOrAdd(given, Translate) : null;

    private static string? Translate(string pattern)
    {
        if (!Compiles(pattern) || HtmlPatternScanner.Scan(pattern) is not { } scanned)
        {
            return null;
        }

        // The browser anchors the pattern itself, so the outermost ^ and $ are said once, by it.
        var html = scanned.Html;
        if (!scanned.Alternates)
        {
            html = html[(scanned.OpensAnchored ? 1 : 0)..(html.Length - (scanned.ClosesAnchored ? 1 : 0))];
        }

        return html.Length > 0 ? html : null;
    }

    private static bool Compiles(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
