namespace Rask.Example.Shared.Features;

// A Bootstrap breadcrumb — the trail of BsBreadcrumbItem links ending in the current page. Passing Href
// makes an item a link; the final item is marked Active, so it renders as plain text with
// aria-current="page". Label renames the wrapping <nav> for assistive tech.
public sealed class BsBreadcrumbDemo : Component
{
    protected override Component? Render() =>
        BsStack(Vertical: true, Gap: 3)[
            BsBreadcrumb()[
                BsBreadcrumbItem(Href: "#")["Home"],
                BsBreadcrumbItem(Href: "#")["Library"],
                BsBreadcrumbItem(Active: true)["Data"]
            ],
            BsBreadcrumb(Label: "Docs sections")[
                BsBreadcrumbItem(Href: "#")["Docs"],
                BsBreadcrumbItem(Active: true)["Bootstrap"]
            ]
        ];
}
