using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;
using Rask.Site.Features;

namespace Rask.Site;

// The guides index rendered as grouped cards (one card per GuideCatalog entry, grouped by category in
// GroupOrder). Rendered by the Guides index (GuidesIndexPage), which is the site root "/" — the
// guides-first showcase leads with these cards.
//
// A component, not a static helper: it returns markup and nothing else, and only a component can reach
// the builder surface (entries are inherited members, so a static class sees none of them). It renders a
// sequence rather than one root, which needs no wrapper element — a `Component` built from a collection
// is a Fragment, and the serializer emits its children inline.
public sealed partial class GuideCards : Component
{
    protected override Component? Render() => [.. Groups()];

    private static IEnumerable<Component> Groups()
    {
        foreach (var group in GuideCatalog.GroupOrder)
        {
            var cards = GuideCatalog.All.Where(g => g.Group == group).ToArray();
            if (cards.Length == 0)
            {
                continue;
            }

            // font-bold AND font-semibold were both on this element, which leaves the winner to
            // whichever Tailwind emits later rather than to the markup. It is a section label above a
            // grid of cards, so it is small and quiet and the cards carry the weight.
            yield return H2
                .Class("mt-8 mb-3 text-xs font-semibold uppercase tracking-widest text-ui-muted")[group];
            yield return Div.Class("grid grid-cols-12 gap-4")[cards.Select(c => (Component)Card(c))];
        }
    }

    // The surface is the kit's own card (UiStyles.Card), so these read as the same object as every
    // other panel the framework draws.
    //
    // The icon sits INLINE with the title rather than in a block above it, and it is small. GuideEntry
    // derives it from the guide's GROUP, so every card under one heading shows the identical glyph —
    // ten copies of the same arrow down a section that is already labelled with the group's name. As a
    // 28px block it was the loudest thing on the page and said nothing; beside the title it reads as a
    // quiet section marker and gives back roughly half the card's height, which is what an index of
    // eighty guides needs on a phone.
    //
    // feature-card / feature-icon / feature-section went with it: no stylesheet in this repo defines
    // any of the three. They are leftovers from a design that was replaced.
    private static Component Card(GuideEntry g) =>
        Div.Class("col-span-12 md:col-span-6 lg:col-span-4").Key(g.Slug)[
            NavLink
                .Href(Features.Routes.GuidePage(g.Slug))
                .ActiveClass("")
                .Class("block h-full no-underline")[
                // A card is a link, so it needs a hover and a focus state. It had neither.
                Div.Class(
                    $"{UiStyles.Card} h-full transition-colors hover:border-ui-brand "
                    + "focus-within:border-ui-brand")[
                    Div.Class("flex items-start gap-3")[
                        UiIcon.Name(g.Icon).Class("mt-0.5 size-5 shrink-0 text-ui-muted"),
                        // min-w-0: without it this flex item cannot shrink below the longest
                        // unbreakable word in the title, and the card widens its grid column.
                        Div.Class("min-w-0")[
                            H3.Class("text-base font-semibold text-ui-ink")[g.Title],
                            P.Class("mt-1 text-sm leading-relaxed text-ui-muted")[g.Blurb]
                        ]
                    ]
                ]
            ]
        ];
}
