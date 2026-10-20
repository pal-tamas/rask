namespace Rask.Site.Features.UiDocs;

/// <summary>How an accordion behaves: what the browser does with it, what the page owns, and its transition.</summary>
internal sealed partial class UiAccordionBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Browser(),
            State(),
            Transition()
        ];

    private static UiDocsTopic Browser() =>
        UiDocsTopic.Key("browser").Heading("No handler, and no script").Paragraphs([
            "An item is a native `<details>` and its heading the `<summary>`, so a click, Enter or Space opens it in "
            + "the browser, Tab moves from heading to heading, and `Exclusive()` is the platform's own `<details name>` "
            + "group.",
            "Closed content is still in the document, so find-in-page reaches it and the browser opens the item "
            + "holding the match. A disabled heading leaves the tab order, takes no pointer and says `aria-disabled`.",
        ]);

    private static UiDocsTopic State() =>
        UiDocsTopic.Key("state").Heading("Who owns which items are open").Paragraphs([
            "`Expanded()` is the state an item starts in. As in Flux, the accordion reports nothing back: the browser "
            + "owns which items are open.",
            "A single collapsible section is an accordion of one item; `Ui.Collapse`, `Ui.AccordionSection` and "
            + "`Ui.Marker` are gone.",
        ]);

    private static UiDocsTopic Transition() =>
        UiDocsTopic.Key("transition").Heading("The transition").Paragraphs([
            "`Transition()` animates the height of the `<details>`' own content box (`::details-content`, with "
            + "`interpolate-size`) over 250ms. A browser without those opens and closes at once, which is what an "
            + "accordion without the step does everywhere.",
        ]);
}
