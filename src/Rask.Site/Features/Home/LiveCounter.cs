namespace Rask.Site;

/// <summary>
/// The live demo tile in the hero — a genuine stateful Rask component. Each click mutates
/// <c>_count</c> and the framework re-renders this subtree automatically (no StateHasChanged),
/// shipping a minimal diff. It is the landing page proving its own thesis in place.
/// </summary>
/// <remarks>
/// It is also what makes the prerender claim checkable end to end: the page is served as real HTML with
/// this tile already rendered at zero, and it only becomes clickable once the bundle boots and takes the
/// page over. If the boot shell were ever lost, this is the thing that would stop working.
/// </remarks>
public sealed partial class LiveCounter : Component
{
    private int _count;

    /// <inheritdoc />
    protected override Component? Render() =>
        // Flux's divided card: a header, the counter, a footer, a hairline between each.
        Ui.Card.Divided[
            Ui.CardHeader[
                // Flux has no status dot: a green badge says this component is the running one.
                Ui.Badge.Sm.Color(Ui.Color.Green).Icon(Ui.IconName.Bolt)["running · /counter"]
            ],
            Ui.CardBody.Class("flex flex-col items-center gap-3 py-8")[
                // Text, not a heading. It is a field label inside a demo card, and the page's only other
                // headings are the section <h2>s below the hero — so as a heading it jumped from <h1>
                // straight to <h3> and broke the document outline a screen reader navigates by.
                Ui.Text["Current count"],
                // .count and .count-btn are a TEST contract, not styling: SiteExampleTests reads the
                // value and clicks the button by these names, and a Playwright locator that resolves to
                // nothing fails by timing out rather than by naming what moved.
                Ui.Heading.Size(Ui.HeadingSize.Xxl).Class("count tabular-nums")[_count],
                Ui.Button.Primary.Class("count-btn").OnClick(() => _count++)["Click me"]
            ],
            Ui.CardFooter[
                Ui.Text.Class("w-full text-center")[
                    "Each click ships a ", Ui.Text.Inline().Variant(Ui.TextVariant.Strong)["~41-byte diff"],
                    " — not a re-render of the page."
                ]
            ]
        ];
}
