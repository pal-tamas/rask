namespace Rask.Site.Features.UiDocs;

/// <summary>How an item lays out: its parts' places, the surface, and what moves on a narrow item.</summary>
internal sealed partial class UiItemBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Parts(),
            Surface(),
            Narrow()
        ];

    private static UiDocsTopic Parts() =>
        UiDocsTopic.Key("parts").Heading("Each part has its place").Paragraphs([
            "An item is a grid of three columns: `Ui.ItemMedia` at the start, `Ui.ItemHeading` with `Ui.ItemSubheading` "
            + "under it in the middle, `Ui.ItemActions` at the end. The media and the actions are centred on the heading "
            + "and subheading, whatever order you write the parts in. `Ui.ItemHeading.Level(3)` makes the heading an "
            + "`<h3>`; without a level it is a `<div>`, outside the document outline.",
        ]);

    private static UiDocsTopic Surface() =>
        UiDocsTopic.Key("surface").Heading("One surface").Paragraphs([
            "`Ui.ItemGroup` draws the bordered, rounded surface and a rule between its items. An item on its own draws "
            + "that surface itself, so a single row needs no group.",
        ]);

    private static UiDocsTopic Narrow() =>
        UiDocsTopic.Key("narrow").Heading("Narrow items").Paragraphs([
            "An item is a size container. Under 24rem of its own width, actions holding a `Ui.Button` with words move "
            + "under the text, so the heading keeps its room. Switches, values, a dropdown's button and icon-only buttons "
            + "stay at the end. It goes by the item's width, not the screen's: the same in a modal or a sidebar.",
        ]);
}
