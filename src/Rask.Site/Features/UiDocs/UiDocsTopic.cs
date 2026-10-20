namespace Rask.Site.Features.UiDocs;

/// <summary>One titled part of a Behaviour section: a heading one level below it, and its paragraphs.</summary>
/// <remarks>Each paragraph is <see cref="UiDocsProse" />: a span between backticks is code.</remarks>
internal sealed partial class UiDocsTopic : Component
{
    /// <summary>What the part is about.</summary>
    public required string Heading { get; set; }

    /// <summary>Its paragraphs, in order.</summary>
    public required IReadOnlyList<string> Paragraphs { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mt-8 space-y-3")[
            Ui.Heading.Level(3).Lg[Heading],
            Paragraphs.Select((paragraph, index) => Ui.Text.Key(index)[UiDocsProse.Of(paragraph)]),
            Children
        ];
}
