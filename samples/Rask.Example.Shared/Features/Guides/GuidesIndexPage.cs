using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// The Guides landing page: the repo's user guides (docs/*.md) rendered on-site, grouped as cards.
// Each card links to /guides/{slug}, where GuidePage renders the markdown with Markdig.
[Route("guides")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GuidesIndexPage : Component
{
    protected override RenderResult Head => Title()["Guides — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Guides",
            "Narrative documentation for the framework — the same guides that ship in the repo's docs/ "
            + "folder, rendered here. Every concept also has a runnable demo in the sidebar; the guides "
            + "link out to them."),
        Div()[BuildGroups()]
    ];

    private IEnumerable<Child> BuildGroups()
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
            yield return Div(Class: "row g-3")[cards.Select(c => (Child)Card(c))];
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
