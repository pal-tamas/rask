namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a card behaves: what each step decides, headers outside a card and inside its body, bleeding to the edges,
///     and a card that is a link.
/// </summary>
internal sealed partial class UiCardBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Steps(),
            Parts(),
            Bleed(),
            Link()
        ];

    private static Component Steps() =>
        UiDocsTopic.Key("steps").Heading("What each step decides").Paragraphs([
            "Each value is a chain step, `Ui.Card.Divided.Sm`; `Inset` is the body treatment, so the divider is "
            + "`.Divider(Ui.CardDivider.Inset)`. A card needs none of its parts: "
            + "`Ui.Card[Ui.CardHeading.Lg[\"Are you sure?\"], P[\"This cannot be undone.\"]]` is padded evenly.",
        ])[
            UiDocsKeys.Column("Step").Rows([
                ("`Body`", "How the header, body and footer are set apart: by space (`Seamless`, the default), a panel "
                           + "in from the edges (`Inset`), a panel out to them (`Flush`), lines (`Divided`), or tinted "
                           + "bands (`Separated`)."),
                ("`Variant`", "The surface: raised (`Default`), two tints (`Muted`, `Soft`), an edge only (`Outline`), "
                              + "or a tint with no edge (`Filled`)."),
                ("`Size`", "`Xs`, `Sm`, `Md` (the default) or `Lg`: the padding, the corners and the space between the parts."),
                ("`Divider`", "With `Divided`, `Ui.CardDivider.Inset` stops the lines at the content's edges."),
                ("`Highlight`", "`false` turns off the faint highlight inside the top edge, in light mode."),
            ])
        ];

    private static UiDocsTopic Parts() =>
        UiDocsTopic.Key("parts").Heading("Headers and footers").Paragraphs([
            "`Ui.CardActions` in a header centres on the heading and tucks into the corner. `Ui.CardHeading` takes "
            + "`Size` (`Base`, `Lg`, `Xl`) and `Level`; without a level it is a `<div>`, outside the document outline.",
            "A header or footer outside a card is a section heading above one: give it the `Size` of the card it sits "
            + "beside. Inside a `Ui.CardBody` it titles a sub-section and takes none of the card's treatment.",
        ]);

    private static UiDocsTopic Bleed() =>
        UiDocsTopic.Key("bleed").Heading("Out to the edges").Paragraphs([
            "`Ui.CardBleed` runs media out to the card's edges: always to the sides, and to the top or bottom when it "
            + "is the first or last thing, rounding only the corners it reaches. The distances are the "
            + "`--ui-bleed-x`, `--ui-bleed-top`, `--ui-bleed-bottom`, `--ui-bleed-top-radius` and "
            + "`--ui-bleed-bottom-radius` variables the card and its body set, so your own content can bleed the same "
            + "way. A card also states `--ui-bleed`, which a table's or a carousel's `Bleed()` reads.",
        ]);

    private static UiDocsTopic Link() =>
        UiDocsTopic.Key("link").Heading("A card that is a link").Paragraphs([
            "There is no `Href` on a card. A link card is a link around a small card, as in Flux: "
            + "`A.Href(url)[Ui.Card.Xs.Class(\"hover:bg-zinc-50 dark:hover:bg-zinc-700\")[…]]`, and nothing inside it "
            + "may be a button.",
        ]);
}
