using Rask.Core.Routing;

namespace Company.RaskServer.Client.Pages;

[Route("/")]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        // A column so the footer sits at the bottom of a short page rather than under the fold.
        Div.Class("flex min-h-screen flex-col bg-base-200")[
            Nav.Class("navbar bg-base-100 shadow-sm")[
                Div.Class("navbar-start")[
                    Span.Class("px-2 text-lg font-semibold tracking-tight")["Company.RaskServer"]
                ],
                Div.Class("navbar-end")[A.Class("link link-hover link-primary").Href("https://rask.sh/docs")["Docs"]]
            ],
            Main.Class("hero grow bg-base-200 py-16")[
                Div.Class("hero-content text-center")[
                    Div.Class("max-w-md")[
                        H1.Class("text-4xl font-bold")["Hello, Rask! 👋"],
                        P.Class("py-4 text-base-content/70")["Your app is running in WebAssembly. What to do next:"],
                        Div.Class("card bg-base-100 w-full max-w-md shadow-sm")[
                            Div.Class("card-body gap-4 text-left")[
                                Ul.Class("space-y-2 text-sm")[
                                    Li[Code.Class("kbd kbd-sm")["rask dev"], " — run it, rebuilt on every save"],
                                    Li["Pages go in ", Code.Class("kbd kbd-sm")["Client/"], ", message records in ", Code.Class("kbd kbd-sm")["Shared/"]],
                                    Li["Edit ", Code.Class("kbd kbd-sm")["Client/Pages/HomePage.cs"], " — the sheet rebuilds from it"]
                                ],
                                Div.Class("card-actions justify-end")[
                                    A
                                        .Class("btn btn-primary")
                                        .Href("https://rask.sh/docs/guides/spa/")["Read the guide"]
                                ]
                            ]
                        ]
                    ]
                ]
            ],
            Footer.Class("footer footer-center bg-base-100 p-4 text-base-content/70")[
                Aside[P["Built with Rask."]]
            ]
        ];
}
