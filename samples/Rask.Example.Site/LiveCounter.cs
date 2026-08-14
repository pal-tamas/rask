namespace Rask.Example.Site;

/// <summary>
/// The live demo tile in the hero's second column — a genuine stateful Rask component. Each click
/// mutates <c>_count</c> and the framework re-renders this subtree automatically (no StateHasChanged),
/// shipping a minimal diff. It is the landing page proving its own thesis in place.
/// </summary>
public sealed partial class LiveCounter : Component
{
    private int _count;

    protected override Component? Render() =>
        Div.Class("card live")[
            Div.Class("card-bar")[
                Span.Class("traf").Style("background:var(--accent)"),
                Span.Class("fn")["running · /counter"]
            ],
            Div.Class("live-body")[
                H3["Current count"],
                Div.Class("count")[_count],
                Button.Class("count-btn").Type("button").OnClick(() => _count++)["Click me"]
            ],
            Div.Class("live-note")[
                "Each click ships a ", B["~41-byte diff"], " — not a re-render of the page."
            ]
        ];
}
