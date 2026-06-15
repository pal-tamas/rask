using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("routing")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class RoutingPage(Navigator nav) : Component
{
    protected override RenderResult Head => Title()["Routing — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Routing",
            "Annotate a component with [Route(\"/path\")]. A module initializer registers it; Router() in your App tree matches the current URL and renders the page."),
        H2(Class: "h4 mt-4 mb-3")["A page is a routed component"],
        CodeSample(
            EmbeddedSource.Read("RoutingAboutPage.cs"),
            Notes:
            "Routes use Blazor-style {param} placeholders. Optional, catch-all (**rest), and type-constrained variants are all supported."),
        H2(Class: "h4 mt-5 mb-3")["Nested layouts with [ParentRoute] + Outlet"],
        CodeSample(
            EmbeddedSource.Read("RoutingLayoutDemo.cs"),
            Notes:
            "Child templates are joined to the parent's. An empty child template (\"\") means \"default child for this layout\". This very showcase is built that way — every page declares [ParentRoute(typeof(ShowcaseLayout))]."),
        H2(Class: "h4 mt-5 mb-3")["Try the live param demo"],
        P(Class: "text-secondary")[
            "The page at ",
            Code()["/users/{id}"],
            " binds the URL segment to a property. Try one of these:"
        ],
        Div(Class: "d-flex flex-wrap gap-2 mb-2")[
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () => nav.Navigate("/users/42"))[I(Class: "bi bi-link-45deg me-1"), "/users/42"],
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () => nav.Navigate("/users/137"))[I(Class: "bi bi-link-45deg me-1"), "/users/137"],
            Button(
                Class: "btn btn-outline-primary btn-sm",
                OnClick: () =>
                    nav.Navigate("/users/ada", new[] { KeyValuePair.Create<string, string?>("tab", "profile") }))[
                I(Class: "bi bi-link-45deg me-1"), "/users/ada?tab=profile"]
        ],
        H2(Class: "h4 mt-5 mb-3")["Reacting to navigation: RouteState.Changed"],
        P(Class: "text-secondary")[
            Code()["RouteState"],
            " raises ",
            Code()["Changed"],
            " whenever the path or query actually changes (reference-equality gated). Subscribe in ",
            Code()["OnMount"],
            " and unsubscribe in ",
            Code()["OnUnmount"],
            ". Useful for components rendered ",
            Em()["above"],
            " the ", Code()["Router"],
            " (sidebars, breadcrumbs, the path display in the showcase header) that need to refresh on every nav, including browser back/forward."
        ],
        CodeSample(
            EmbeddedSource.Read("PathDisplay.cs"),
            Notes:
            "The handler is just StateHasChanged — the framework already knows how to coalesce the resulting render with whatever else the dispatcher is processing. Always pair the subscribe with the unsubscribe in OnUnmount; otherwise the RouteState keeps a strong reference to the (already-unmounted) component.")
    ];
}
