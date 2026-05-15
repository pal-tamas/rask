using static Company.RaskServer.Routes;

namespace Company.RaskServer;

public sealed class App : Component
{
    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head()[
                    Meta("utf-8"),
                    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                    Title()["Company.RaskServer"],
                    RaskScopedStyles()
                ],
                Body()[
                    Nav()[
                        NavLink(HomePage())["Home"],
                        " | ",
                        NavLink(Counter())["Counter"],
                        " | ",
                        NavLink(Weather())["Weather"]
                    ],
                    Hr(),
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
