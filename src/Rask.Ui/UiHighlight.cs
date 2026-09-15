namespace Rask.Ui;

/// <summary>
/// Search-result text with each matched term wrapped in <c>&lt;mark&gt;</c> — what <c>FullText.Highlight</c> and
/// <c>FullText.Snippet</c> return, rendered safely.
/// </summary>
/// <remarks>
/// <para>
/// Those functions mark a match between U+E000 and U+E001 rather than with HTML, because the text is whatever was
/// stored in the row: a highlight rendered as markup would let anyone who can write a row inject script into every
/// page that searches it. This splits on the markers instead, so every character of the text is encoded and the only
/// elements in the output are the <c>&lt;mark&gt;</c>s it adds.
/// </para>
/// <para>
/// A start marker with no end marks the rest of the text; an end marker with no start is dropped.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// UiHighlight.Text(hit.Excerpt)
/// </code>
/// </example>
public sealed partial class UiHighlight : Component
{
    // FullText.MatchStart / MatchEnd in Rask.Data, which this package does not reference; a test pins them equal.
    internal const char MatchStart = '';
    internal const char MatchEnd = '';

    public new required string Text { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var parts = new List<Component>();
        var text = Text ?? string.Empty;
        var start = 0;
        var marking = false;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not (MatchStart or MatchEnd))
            {
                continue;
            }

            Flush(parts, text[start..i], marking);
            start = i + 1;

            if (text[i] == MatchStart)
            {
                marking = true;
            }
            else if (marking)
            {
                marking = false;
            }
        }

        Flush(parts, text[start..], marking);

        return Span.Class(Class)[parts];
    }

    private static void Flush(List<Component> parts, string segment, bool marked)
    {
        if (segment.Length == 0)
        {
            return;
        }

        // A string child is a Text node, which encodes it.
        parts.Add(marked ? Mark[segment] : segment);
    }
}
