using Rask.Core;
using Rask.Core.Components;
using Rask.Example.Shared.Features;

namespace Rask.Example.Shared;

// The guides index rendered as grouped cards (one card per GuideCatalog entry, grouped by category in
// GroupOrder). Rendered by the Guides index (GuidesIndexPage), which is the site root "/" — the
// guides-first showcase leads with these cards. Built with component factories, so call it from inside a
// render (a LiveRenderContext is required).
public static class GuideCards
{
    public static IEnumerable<Component> Render()
    {
        foreach (var group in GuideCatalog.GroupOrder)
        {
            var cards = GuideCatalog.All.Where(g => g.Group == group).ToArray();
            if (cards.Length == 0)
            {
                continue;
            }

            yield return H2(Class: Bs.Join(Font.Bold, Txt.Uppercase, Txt.Color(BsColor.Secondary),
                Margin.Top(4), Margin.Bottom(3), "h6", "feature-section"))[group];
            yield return Div(Class: "row g-3")[cards.Select(c => (Component)Card(c))];
        }
    }

    private static Component Card(GuideEntry g) =>
        Div(Class: "col-md-6 col-lg-4", Key: g.Slug)[
            NavLink(Href: Features.Routes.GuidePage(g.Slug), ActiveClass: "", Class: "text-decoration-none")[
                BsCard(Class: Bs.Join(Sizing.H(100), Border.None, Shadow.Sm, "feature-card"))[
                    BsCardBody(Class: "p-4")[
                        Div(Class: "feature-icon mb-3")[I(Class: $"bi {g.Icon}")],
                        H3(Class: Bs.Join(Font.Semibold, Margin.Bottom(2), "h6", "text-body"))[g.Title],
                        P(Class: Bs.Join(Txt.Color(BsColor.Secondary), Margin.Bottom(0), "small"))[g.Blurb]
                    ]
                ]
            ]
        ];
}
