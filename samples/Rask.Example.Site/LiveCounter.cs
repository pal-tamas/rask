using Rask.Ui;

namespace Rask.Example.Site;

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
        Div.Class("rounded-2xl border border-ui-line bg-ui-bg")[
            Div.Class("flex items-center gap-2 border-b border-ui-line bg-ui-well px-4 py-2.5")[
                // The kit's status dot, unchanged from the console — where it reports whether a queue is
                // healthy. Here it reports that this component is the running one.
                UiStatusDot.Label("running · /counter").Tone(UiTone.Ok)
            ],
            Div.Class("flex flex-col items-center gap-3 px-6 py-8")[
                H3.Class("text-sm font-medium text-ui-muted")["Current count"],
                // .count and .count-btn are a TEST contract, not styling: SiteExampleTests reads the
                // value and clicks the button by these names, and a Playwright locator that resolves to
                // nothing fails by timing out rather than by naming what moved.
                Div.Class("count text-5xl font-semibold tabular-nums tracking-tight text-ui-ink")[_count],
                Button
                    .Class(
                        "count-btn inline-flex min-h-11 items-center rounded-xl bg-ui-ink px-5 text-sm "
                        + "font-semibold text-ui-bg transition-colors hover:bg-ui-ink/90")
                    .Type("button")
                    .OnClick(() => _count++)["Click me"]
            ],
            Div.Class("border-t border-ui-line px-4 py-3 text-center text-xs text-ui-muted")[
                "Each click ships a ", B.Class("text-ui-ink")["~41-byte diff"], " — not a re-render of the page."
            ]
        ];
}
