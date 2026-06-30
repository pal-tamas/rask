using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Renders one guide: docs/{slug}.md, embedded and read by GuideCatalog, rendered by the Markdown
// component. The slug comes straight from the route, so /guides/routing renders docs/routing.md.
[Route("guides/{slug}")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GuidePage : Component
{
    [RouteParam] public string Slug { get; set; } = string.Empty;

    protected override RenderResult Head => Title()[$"{GuideCatalog.TitleFor(Slug)} — Guides — Rask"];

    protected override RenderResult Render()
    {
        var backLink = NavLink(Href: Features.Routes.GuidesIndexPage(), ActiveClass: "",
            Class: Bs.Join(Display.InlineFlex(), Flex.Align(BsAlign.Center), Margin.Bottom(3),
                Txt.DecorationNone, "small"))[
            BsIcon(Name: BsIconName.ArrowLeft, Class: "me-1"), "All guides"
        ];

        var source = GuideCatalog.ReadMarkdown(Slug);
        return source is null
            ?
            [
                backLink,
                BsAlert(Color: BsColor.Warning)[$"No guide found for “{Slug}”."]
            ]
            :
            [
                backLink,
                Markdown(source)
            ];
    }
}
