using static Company.RaskWasm.Routes;

namespace Company.RaskWasm;

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
                    Title(Children: ["Company.RaskWasm"]),
                    RaskScopedStyles()
                ]),
                Body(Children:
                [
                    Nav(Children:
                    [
                        NavLink(HomePage(), Children: ["Home"]),
                        " | ",
                        NavLink(Counter(), Children: ["Counter"]),
                        " | ",
                        NavLink(Weather(), Children: ["Weather"])
                    ]),
                    Hr(),
                    Router(),
                    RaskRuntimeScript()
                ])
            ])
        );
}
