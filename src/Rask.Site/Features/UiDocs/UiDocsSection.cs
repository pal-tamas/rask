namespace Rask.Site.Features.UiDocs;

/// <summary>One titled part of a UI tab page: a heading the outline lists, and what is under it.</summary>
internal sealed partial class UiDocsSection : Component
{
    /// <summary>What the part is about.</summary>
    public required string Heading { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Section.Class("mt-12 space-y-3")[
            Ui.Heading.Level(2).Xl[Heading],
            Children
        ];
}
