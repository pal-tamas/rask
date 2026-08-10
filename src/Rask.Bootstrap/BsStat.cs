namespace Rask.Bootstrap;

// A stat tile: one number, what it means, and optionally why you should care. The shape every
// dashboard needs and Bootstrap has no primitive for — built from BsCard rather than raw markup, per
// the library convention that a Bs component composes other Bs components.
//
// Tone drives the number's colour, not the card's background: a wall of coloured panels reads as
// decoration, whereas one red number among grey ones reads as a signal. Callers are expected to leave
// Tone unset for ordinary counters and set Danger only when the value genuinely demands action (a
// dead-letter count above zero, say).
public sealed partial class BsStat : BsBlock
{
    // The number (or short string) — the reason the tile exists.
    public required string Value { get; set; }

    // What the number counts, shown above it in small caps.
    public new required string Label { get; set; }

    // Optional supporting line under the number: a unit, a threshold, a timestamp.
    public new string? Caption { get; set; }

    // Colours the value. Leave unset for the neutral default.
    public BsColor? Tone { get; set; }

    // Optional icon shown beside the label.
    public BsIconName? Icon { get; set; }

    // Renders the whole tile as a link to this URL, so a tile can be the way into its detail page.
    public string? Href { get; set; }

    protected override Component? Render()
    {
        var body = BsCardBody.Class("py-3")[
            Div.Class("d-flex align-items-center gap-2 text-body-secondary text-uppercase small fw-semibold")[
                Icon is { } icon ? BsIcon.Name(icon) : null,
                Span[Label]
            ],
            Div.Class(BsClass.Join("fs-3 fw-semibold lh-1 mt-2", Tone is { } t ? t.Text() : null))[Value],
            Caption is { } caption ? Div.Class("small text-body-secondary mt-1")[caption] : null
        ];

        // A linked tile must not look like body text, and must not lose the card's own affordances —
        // stretched-link would need positioning on the card, so the anchor wraps it instead.
        return Href is { } href
            ? A.Href(href).Class(BsClass.Join("text-decoration-none text-reset d-block h-100", Class)).Id(Id)[
                BsCard.Class("h-100")[body]
            ]
            : BsCard.Id(Id).Class(BsClass.Join("h-100", Class))[body];
    }
}
