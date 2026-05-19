using static Company.RaskWasmHosted.Wasm.Routes;

namespace Company.RaskWasmHosted.Wasm;

public sealed class App : Component
{
    // App-level head contributions splice into the framework-managed <head>
    // via the Component? Head override. Title is singleton — any page that
    // overrides Head with its own Title supersedes this fallback for the tab.
    protected override Component? Head => Fragment()[
        Title()["Company.RaskWasmHosted"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1")
    ];

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head(),
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
