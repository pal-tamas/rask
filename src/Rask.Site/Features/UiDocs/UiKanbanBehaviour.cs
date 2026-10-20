namespace Rask.Site.Features.UiDocs;

/// <summary>How a kanban board behaves: what a card is, what moves it, how a board fits its place, and what is left out.</summary>
internal sealed partial class UiKanbanBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Cards(),
            Moving(),
            Fitting()
        ];

    private static UiDocsTopic Cards() =>
        UiDocsTopic.Key("cards").Heading("A card is something to read").Paragraphs([
            "A card is a `<div>` until `.As(Ui.KanbanCardAs.Button)` makes the whole card a `<button>`: focused with "
            + "Tab, pressed with Enter or Space, lighter under the pointer. `OnClick` is what it does. `Header` goes "
            + "above the heading (badges), `Footer` under it (an icon, avatars), and children replace the heading.",
        ]);

    private static UiDocsTopic Moving() =>
        UiDocsTopic.Key("moving").Heading("It moves nothing").Paragraphs([
            "Exactly as Flux's: no card is draggable, no key reorders one, and there is no drop event. Adding or "
            + "moving a card is your page changing its own lists and rendering again; for dragging, `DragDrop` is the "
            + "framework's primitive.",
        ]);

    private static UiDocsTopic Fitting() =>
        UiDocsTopic.Key("fitting").Heading("Fitting a board in").Paragraphs([
            "A column does not shrink, so a board wider than its place needs a box that scrolls: "
            + "`Div.Class(\"overflow-x-auto\")[Ui.Kanban[…]]`.",
            "Flux's `badge` prop on the column header is not carried: no example on Flux's page draws it, so there is "
            + "nothing to measure it against. A `Ui.Badge` of your own goes in as a child, beside the heading you write "
            + "there.",
        ]);
}
