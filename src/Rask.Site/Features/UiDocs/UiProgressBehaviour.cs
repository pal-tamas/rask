namespace Rask.Site.Features.UiDocs;

/// <summary>How a progress bar behaves: what it announces, how the bar is worked out and moves, and its look.</summary>
internal sealed partial class UiProgressBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Announced(),
            Bar(),
            Look()
        ];

    private static UiDocsTopic Announced() =>
        UiDocsTopic.Key("announced").Heading("What it announces").Paragraphs([
            "It IS the `<div role=\"progressbar\">`, with `aria-valuemin` 0, and `aria-valuenow` and `aria-valuemax` "
            + "the value and maximum exactly as given.",
            "It is a `UiElement`, so the name a screen reader needs is `.Aria(\"label\", \"Upload\")` or "
            + "`.Aria(\"labelledby\", id)`. A name written at the call site wins, as everywhere in the kit.",
        ]);

    private static UiDocsTopic Bar() =>
        UiDocsTopic.Key("bar").Heading("The bar").Paragraphs([
            "The bar is `Value` over `Max`, held between empty and full, and moves to a new value over 300ms.",
            "The same share is on the element as `--ui-progress` (a number, 0–100) and `--ui-progress-percentage`, for "
            + "a label or a ring drawn from the same figure.",
        ]);

    private static UiDocsTopic Look() =>
        UiDocsTopic.Key("look").Heading("Size and colour").Paragraphs([
            "A 6px track the width of its container, written at zero specificity so `.Class(\"h-3\")` makes it "
            + "taller. Unset, the bar is the accent; a `Color` is the 600 of its hue, and its 400 on a dark surface, "
            + "as measured on Flux.",
            "There is no radial progress: Flux has none, and `Ui.RadialProgress` is gone.",
        ]);
}
