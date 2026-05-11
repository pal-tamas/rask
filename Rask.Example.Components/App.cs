using Rask.Core;
using static Rask.Core.Tags;
using static Rask.Example.Components.Routes;

namespace Rask.Example.Components;

public sealed class App : Component
{
    public override Component Render() =>
        Fragment(
            Doctype(),
            Html("en", Children:
            [
                Head(Children:
                [
                    Meta("utf-8"),
                    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                    Title(Children: ["Rask Example"]),
                    Link(Rel: "icon", Href: "/favicon.svg", Type: "image/svg+xml"),
                    Link(Rel: "stylesheet",
                        Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css",
                        CrossOrigin: "anonymous"),
                    RaskScopedStyles()
                ]),
                Body(Class: "bg-light", Children:
                [
                    Nav(Class: "navbar navbar-expand bg-white border-bottom mb-4", Children:
                    [
                        Div(Class: "container", Children:
                        [
                            NavLink(HomePage(), Class: "navbar-brand", Children: ["Rask"]),
                            Ul(Class: "navbar-nav me-auto", Children:
                            [
                                Li(Class: "nav-item", Children:
                                [
                                    NavLink(HomePage(), Class: "nav-link", Children: ["Home"])
                                ]),
                                Li(Class: "nav-item", Children:
                                [
                                    NavLink(Counter(), Class: "nav-link", Children: ["Counter"])
                                ]),
                                Li(Class: "nav-item", Children:
                                [
                                    NavLink(Weather(), Class: "nav-link", Children: ["Weather"])
                                ])
                            ]),
                            global::Rask.Example.Components.Components.UserBadge()
                        ])
                    ]),
                    Div(Class: "container", Children: [Router()]),
                    RaskRuntimeScript()
                ])
            ])
        );
}
