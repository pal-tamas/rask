namespace Rask.Example.Shared;

// A "See also" row that cross-links a demo page to its narrative guide(s). The heavy conceptual prose
// lives in the rendered docs/*.md guides; a demo page stays demo-first and just points at the matching
// guide with this one-liner. Each link is a SPA-routed NavLink to /guides/{slug}.
[global::Rask.Core.RaskMarkup]
internal static partial class SeeAlso
{
    public static Component Guides(params (string Slug, string Title)[] guides)
    {
        var children = new List<Component>
        {
            Span.Class("text-ui-muted font-semibold")[
                UiIcon.Name(UiIconName.Book).Class("me-1"), "See also"
            ]
        };
        children.AddRange(guides.Select(g => (Component)NavLink
            .Href(Features.Routes.GuidePage(g.Slug))
            .ActiveClass("")
            .Class("see-also-link no-underline")
            .Key(g.Slug)[g.Title]));

        return Div
            .Class("flex flex-wrap items-center gap-2 mt-5 pt-3 border-t text-sm")[children];
    }
}
