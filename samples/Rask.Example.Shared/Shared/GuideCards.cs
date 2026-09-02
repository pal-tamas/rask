using Rask.Core;
using Rask.Core.Components;
using Rask.Example.Shared.Features;
using Rask.Html.Components;

namespace Rask.Example.Shared;

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

            yield return H2
                .Class("font-bold uppercase text-slate-500 mt-4 mb-3 text-base font-semibold feature-section")[group];
            yield return Div.Class("grid grid-cols-12 gap-4")[cards.Select(c => (Component)Card(c))];
        }
    }

    private static Component Card(GuideEntry g) =>
        Div.Class("md:col-span-6 lg:col-span-4").Key(g.Slug)[
            NavLink.Href(Features.Routes.GuidePage(g.Slug)).ActiveClass("").Class("no-underline")[
                Div.Class($"{Tw.Card} h-full border-0 shadow-sm feature-card")[
                    Div.Class($"{Tw.CardBody} p-4")[
                        Div.Class("feature-icon mb-3")[Icon.Name(g.Icon).Class("text-2xl")],
                        H3.Class("font-semibold mb-2 text-base text-slate-900 dark:text-slate-100")[g.Title],
                        P.Class("text-slate-500 mb-0 text-sm")[g.Blurb]
                    ]
                ]
            ]
        ];
}
