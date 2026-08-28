namespace Rask.Example.Site;

/// <summary>
/// The live demo tile in the hero's second column — a genuine stateful Rask component. Each click
/// mutates <c>_count</c> and the framework re-renders this subtree automatically (no StateHasChanged),
/// shipping a minimal diff. It is the landing page proving its own thesis in place.
/// </summary>
public sealed partial class LiveCounter : Component
{
    private int _count;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("rounded-2xl border border-line bg-panel")[
            Div.Class("flex items-center gap-2 border-b border-line bg-panel-2 px-4 py-2.5")[
                Span.Class("size-2.5 shrink-0 rounded-full").Style("background:var(--color-accent)"),
                Span.Class("font-mono text-xs text-muted")["running · /counter"]
            ],
            Div.Class("flex flex-col items-center gap-3 px-6 py-8")[
                H3.Class("text-sm font-medium text-muted")["Current count"],
                // .count and .count-btn are a TEST contract, not styling: SiteExampleTests reads the
                // value and clicks the button by these names, and a Playwright locator that resolves to
                // nothing fails by timing out rather than by naming what moved.
                Div.Class("count text-5xl font-semibold tabular-nums tracking-tight text-ink")[_count],
                Button
                    .Class("count-btn inline-flex items-center rounded-xl bg-accent px-5 py-2.5 text-sm font-semibold text-white hover:bg-accent-2")
                    .Type("button")
                    .OnClick(() => _count++)["Click me"]
            ],
            Div.Class("border-t border-line px-4 py-3 text-center text-xs text-muted")[
                "Each click ships a ", B.Class("text-ink-soft")["~41-byte diff"], " — not a re-render of the page."
            ]
        ];
}
