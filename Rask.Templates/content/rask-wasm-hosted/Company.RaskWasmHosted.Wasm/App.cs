using static Company.RaskWasmHosted.Wasm.Routes;

namespace Company.RaskWasmHosted.Wasm;

public sealed class App : Component
{
    public override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head()[
                    Meta("utf-8"),
                    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
                    Title()["Company.RaskWasmHosted"],
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
