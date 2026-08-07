namespace Rask.Example.Shared.Features;

// The live result for the Composition guide's "Component tiers" section: the three ways to
// author a reusable unit, side by side. Only the third (TierStatefulCounterDemo) holds state —
// click its button and just that card re-renders through the live diff. The three source files
// shown in the code panes above are TierStaticHelperDemo / TierStatelessGreetingDemo /
// TierStatefulCounterDemo; this composer is intentionally kept out of the panes so the teaching
// artifact stays the three tiers themselves.
public sealed partial class ComponentTiersDemo : Component
{
    protected override Component? Render() =>
        BsRow(Id: "component-tiers", Gutter: 3)[
            Tier("Tier 0 — static method", "Inlined markup. No instance, no state, no lifecycle.",
                TierStaticHelperDemo()),
            Tier("Tier 1 — stateless component", "Props in → markup out. Identity + lifecycle, no fields.",
                TierStatelessGreetingDemo()),
            Tier("Tier 2 — stateful component", "Private field mutated in a handler → auto re-render.",
                TierStatefulCounterDemo())
        ];

    // A Tier-0 static helper itself — the pattern the first card describes — reused for each column.
    private static Component Tier(string title, string blurb, Component body) =>
        BsCol(Md: 4)[
            BsCard(Class: "h-100")[
                BsCardBody()[
                    H6(Class: "fw-semibold mb-1")[title],
                    P(Class: "small text-secondary")[blurb],
                    body
                ]
            ]
        ];
}
