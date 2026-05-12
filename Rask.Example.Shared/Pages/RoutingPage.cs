using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("routing")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class RoutingPage(Navigator nav) : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Routing",
                "Annotate a component with [Route(\"/path\")]. A module initializer registers it; Router() in your App tree matches the current URL and renders the page."),
            H2(Class: "h4 mt-4 mb-3", Children: ["A page is a routed component"]),
            Demos.Components.CodeSample(
                """
                [Route("/about")]
                public sealed class AboutPage : Component
                {
                    public override Component Render() =>
                        H1(Children: ["About"]);
                }
                """,
                Notes:
                "Routes use Blazor-style {param} placeholders. Optional, catch-all (**rest), and type-constrained variants are all supported."),
            H2(Class: "h4 mt-5 mb-3", Children: ["Nested layouts with [ParentRoute] + Outlet"]),
            Demos.Components.CodeSample(
                """
                [Route("/")]
                public sealed class Layout : Component
                {
                    public override Component Render() =>
                        Div(Children: [
                            Nav(/* sidebar */),
                            Main(Children: [Outlet()])   // children render here
                        ]);
                }

                [Route("about"), ParentRoute(typeof(Layout))]
                public sealed class AboutPage : Component { ... }

                // /about now matches Layout → AboutPage.
                """,
                Notes:
                "Child templates are joined to the parent's. An empty child template (\"\") means \"default child for this layout\". This very showcase is built that way — every page declares [ParentRoute(typeof(ShowcaseLayout))]."),
            H2(Class: "h4 mt-5 mb-3", Children: ["Try the live param demo"]),
            P(Class: "text-secondary", Children:
            [
                "The page at ",
                Code(Children: ["/users/{id}"]),
                " binds the URL segment to a property. Try one of these:"
            ]),
            Div(Class: "d-flex flex-wrap gap-2 mb-2", Children:
            [
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/users/42"),
                    Children: [I(Class: "bi bi-link-45deg me-1"), "/users/42"]),
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () => nav.Navigate("/users/137"),
                    Children: [I(Class: "bi bi-link-45deg me-1"), "/users/137"]),
                Button(
                    Class: "btn btn-outline-primary btn-sm",
                    OnClick: () =>
                        nav.Navigate("/users/ada", new[] { KeyValuePair.Create<string, string?>("tab", "profile") }),
                    Children: [I(Class: "bi bi-link-45deg me-1"), "/users/ada?tab=profile"])
            ])
        );
}
