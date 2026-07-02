namespace Rask.Example.Shared;

// A "See also" row that cross-links a demo page to its narrative guide(s). The heavy conceptual prose
// lives in the rendered docs/*.md guides; a demo page stays demo-first and just points at the matching
// guide with this one-liner. Each link is a SPA-routed NavLink to /guides/{slug}.
internal static class SeeAlso
{
    public static Component Guides(params (string Slug, string Title)[] guides)
    {
        var children = new List<Component>
        {
            Span(Class: Bs.Join(Txt.Color(BsColor.Secondary), Font.Semibold))[
                BsIcon(Name: BsIconName.JournalText, Class: "me-1"), "See also"
            ]
        };
        children.AddRange(guides.Select(g => (Component)Rask.Core.Components.Generated.NavLink(
            Href: Features.Routes.GuidePage(g.Slug), ActiveClass: "",
            Class: Bs.Join("see-also-link", Txt.DecorationNone), Key: g.Slug)[g.Title]));

        return Div(Class: Bs.Join(Display.Flex(), Flex.Wrap(), Flex.Align(BsAlign.Center), Flex.Gap(2),
            Margin.Top(5), Padding.Top(3), Border.Top, "small"))[children];
    }
}
