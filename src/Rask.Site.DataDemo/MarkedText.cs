using Rask.Data;

namespace Rask.Site.DataDemo;

/// <summary>
///     What <c>FullText.Highlight</c> and <c>FullText.Snippet</c> return, with each match in a <c>&lt;mark&gt;</c>.
/// </summary>
/// <remarks>
///     The markers are characters, not HTML, so the text is split on them and every piece is encoded: a row's text can
///     never become markup. A start marker with no end marks the rest of the text; an end marker with no start is dropped.
/// </remarks>
public sealed partial class MarkedText : Component
{
    public new required string Text { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var parts = new List<Component>();
        var start = 0;
        var marking = false;

        for (var i = 0; i < Text.Length; i++)
        {
            if (Text[i] != FullText.MatchStart && Text[i] != FullText.MatchEnd)
            {
                continue;
            }

            Add(parts, Text[start..i], marking);
            start = i + 1;
            marking = Text[i] == FullText.MatchStart;
        }

        Add(parts, Text[start..], marking);

        return Span.Class(Class)[parts];
    }

    // A string child is a Text node, which encodes it.
    private static void Add(List<Component> parts, string piece, bool marked)
    {
        if (piece.Length > 0)
        {
            parts.Add(marked ? Mark[piece] : piece);
        }
    }
}
