namespace Rask.Site.Features.UiKit;

/// <summary>One example on a component's page: a title, a line on what it shows, and the example.</summary>
internal sealed partial class UiKitExample : Component
{
    /// <summary>What the example is called — Flux's name for it, where it has one.</summary>
    public required string Heading { get; set; }

    /// <summary>A line on what it shows.</summary>
    public string? Blurb { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mb-8")[
            Ui.Heading.Level(3).Lg[Heading],
            Blurb is null ? null : Ui.Text.Sm.Class("mt-1 mb-3")[Blurb],
            Children
        ];
}
