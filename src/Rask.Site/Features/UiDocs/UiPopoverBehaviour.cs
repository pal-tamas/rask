namespace Rask.Site.Features.UiDocs;

/// <summary>How a popover behaves: who opens it, where focus goes, what it locks, and what Flux's attributes become.</summary>
internal sealed partial class UiPopoverBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("opening").Heading("Opening and closing").Paragraphs([
                "For what is not a menu: a form, a set of filters, a card about a person. It is the second child of the "
                + "`Ui.Dropdown` that holds its trigger, exactly as `flux:popover` sits in `flux:dropdown`, and "
                + "everything about where it opens and what opens it is the dropdown's, its viewport placement too.",
                "The panel is a `[popover]` the trigger names with `popovertarget`, so the browser opens it, lifts it "
                + "into the top layer and closes it on Escape and on a press anywhere else. It is no menu and says no "
                + "role.",
            ]),
            UiDocsTopic.Key("focus").Heading("Focus").Paragraphs([
                "Focus stays on the trigger as it opens: the panel takes none, and Tab walks from there into the panel. "
                + "Shift+Tab off the trigger, or Tab past the panel's last control, closes it.",
            ]),
            UiDocsTopic.Key("lock").Heading("The page behind").Paragraphs([
                "While one a press opened is up, the page behind neither scrolls nor takes the pointer "
                + "(`data-rask-lock`), so a press on anything else only closes it. One the pointer opened, `Hover()`, "
                + "locks nothing; a press and Enter still open it too.",
            ]),
            UiDocsTopic.Key("open").Heading("Layout while open").Paragraphs([
                "An open panel carries `data-open` from the moment it opens. The runtime writes it in the browser "
                + "(`data-rask-open-mark`), so no round trip stands between the press and the layout, and it is what "
                + "Flux's own `data-open:flex` styles by. A closed panel is never displayed, whatever layout its "
                + "`Class` gives it.",
            ]),
            UiDocsTopic.Key("flux").Heading("From Flux").Paragraphs([
                "Flux's attributes, as steps:",
            ])[
                UiDocsKeys.Column("Flux").Rows([
                    ("`flux:dropdown position=\"top\" align=\"end\"`", "`Ui.Dropdown.Top.End`"),
                    ("`flux:dropdown gap=\"16\" offset=\"32\"`", "`Ui.Dropdown.Gap(16).Offset(32)`"),
                    ("`flux:dropdown hover`", "`Ui.Dropdown.Hover()`: the pointer opens it; a press and Enter still do."),
                    ("`flux:dropdown wire:model=\"open\"`", "`Ui.Dropdown.Open(_open).OnToggle(open => _open = open)`"),
                    ("`flux:popover class=\"w-80 p-0\"`", "`Ui.Popover.Class(\"w-80 p-0\")`: its own width, padding, "
                                                          + "radius and shadow weigh nothing, so yours win."),
                ])
            ]
        ];
}
