namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a modal behaves: what the browser and the runtime do, closing against dismissing, its width, its close
///     button, and the page owning its state.
/// </summary>
internal sealed partial class UiModalBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Opening(),
            Closing(),
            Size(),
            State(),
            Shapes()
        ];

    private static Component Opening() =>
        UiDocsTopic.Key("opening").Heading("Who opens it").Paragraphs([
            "No handler runs. The trigger makes its button an HTML invoker (`command=\"show-modal\" commandfor`), so the "
            + "browser supplies the top layer, an inert page behind it so Tab cannot wander out, Escape, and focus "
            + "handed back to the trigger on close.",
            "What Flux adds in script, the dialog asks Rask's runtime for by attribute: `data-rask-modal` for a click "
            + "outside and for which of the two dismissals are allowed, `data-rask-lock=\"scroll\"` for the page held "
            + "still behind it, and the invoker commands themselves in a browser that has none (before Chrome 135, "
            + "Firefox 144 and Safari 26.2).",
            "While a modal is open the page behind it does not scroll, and keeps its scrollbar's gutter where a "
            + "scrollbar is showing, so it neither shifts sideways nor moves from where it was scrolled to.",
        ]);

    private static Component Closing() =>
        UiDocsTopic.Key("closing").Heading("Closing, and dismissing").Paragraphs([
            "A modal draws the panel and the close button in its corner; what it holds (a heading, fields, a row of "
            + "buttons) is yours, as in Flux.",
        ])[
            UiDocsKeys.Column("Step").Rows([
                ("`Dismissible(false)`", "A click outside does nothing."),
                ("`Escapable(false)`", "Escape does nothing. Alone, it still closes on a click outside: all four "
                                       + "combinations of the two switches work."),
                ("`Closable(false)`", "No close button in the corner."),
                ("`OnClose`", "Hears every way it closed."),
                ("`OnCancel`", "Hears only a dismissal, Escape or a click outside, and runs before `OnClose`: a dialog "
                               + "holding a draft can throw it away when the reader backs out and keep it when they "
                               + "press a button that closes it. The close button and a `Ui.ModalClose` are not "
                               + "dismissals."),
            ]),
            Ui.Text.Class("mt-3")[UiDocsProse.Of(
                "Both are the dialog's own events: a dismissal raises `cancel` before `close`, in every browser.")]
        ];

    private static Component Size() =>
        UiDocsTopic.Key("size").Heading("Width, and the close button").Paragraphs([
            "A modal with no width of its own is as wide as what it holds, between Flux's two defaults: at least 20rem "
            + "and at most 36rem (a side flyout: at least 25rem from `md`). A width handed to `Class` replaces them: "
            + "`md:w-96` for a form, and `min-w-[22rem]` for a confirmation, which is what Flux's own example writes; "
            + "a short question over two buttons is a narrow box without it.",
            "The close button is 32px, 16px in from the top and from the end edge, at every width. The heading keeps "
            + "no room for it, in Flux as here: the panel's padding is 24px, so a first line that fills the panel (and "
            + "a one-line question the modal hugs always does) ends 24px under the button.",
            "Flux's examples avoid that the way the confirmation above does: a short heading, \"Delete project?\", "
            + "with the sentence in a `Ui.Text` under it. Where the heading is the sentence, "
            + "`Ui.Heading.Class(\"pe-8\")` keeps it clear of the button.",
        ]);

    private static Component State() =>
        UiDocsTopic.Key("state").Heading("When the page owns it").Paragraphs([
            "Where Flux controls a modal from PHP (`Flux::modal('confirm')->show()`), a Rask page owns the state "
            + "instead: `Ui.Modal.Open(_confirming).OnClose(() => _confirming = false)[…]`, which is Rask's "
            + "`wire:model`.",
            "It is the same modal: the dialog says `data-rask-modal-open` and the runtime shows and closes it to "
            + "match, so it is in the top layer behind the same backdrop, Escape and a click outside run `OnCancel` "
            + "then `OnClose`, and focus returns when it closes. `OnClose` is where the page hears that the reader "
            + "closed it: set the field there, or the page goes on saying it is open.",
            "A modal with neither a `Name` nor `Open` is open for as long as the page renders it. A modal without a "
            + "`Key` beside keyed ones is fine: it keeps its instance by its order among the unkeyed modals of the "
            + "page.",
        ]);

    private static Component Shapes() =>
        UiDocsTopic.Key("shapes").Heading("Shapes").Paragraphs([
            "Each is a step on the same modal. `Ui.Drawer` is gone: a panel that slides in from an edge is a flyout.",
        ])[
            UiDocsKeys.Column("Step").Rows([
                ("`Flyout()`", "Against the right edge, at full height."),
                ("`Flyout().Left`, `Flyout().Bottom`", "Against another edge: `Ui.ModalPosition` is Right, Left or "
                                                       + "Bottom."),
                ("`Flyout().Floating`", "Standing off the edges, rounded."),
                ("`Variant(Ui.ModalVariant.Bare)`", "No panel and no close button: what it holds is all there is."),
                ("`Scroll(Ui.ModalScroll.Body)`", "The whole layer scrolls, not the dialog."),
                ("`Ui.ModalTrigger.Shortcut(\"mod+k\")`", "Opens it from anywhere on the page: Flux's `cmd.k`."),
            ])
        ];
}
