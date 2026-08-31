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
        Div.Class("grid grid-cols-12 gap-4").Id("component-tiers")[
            Tier("Tier 0 — static method", "Inlined markup. No instance, no state, no lifecycle.",
                TierStaticHelperDemo),
            Tier("Tier 1 — stateless component", "Props in → markup out. Identity + lifecycle, no fields.",
                TierStatelessGreetingDemo),
            Tier("Tier 2 — stateful component", "Private field mutated in a handler → auto re-render.",
                TierStatefulCounterDemo)
        ];

    // A Tier-0 static helper itself — the pattern the first card describes — reused for each column.
    private static Component Tier(string title, string blurb, Component body) =>
        Div.Class("md:col-span-4")[
            Div.Class($"{Ui.Card} h-full")[
                Div.Class(Ui.CardBody)[
                    H6.Class("font-semibold mb-1")[title],
                    P.Class("text-sm text-slate-500 dark:text-slate-400")[blurb],
                    body
                ]
            ]
        ];
}
