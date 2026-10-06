namespace Company.RaskServer.Features.Home;

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
                        P.Class("py-4 text-base-content/70")["Your app is running. What to do next:"],
                        Div.Class("card bg-base-100 w-full max-w-md shadow-sm")[
                            Div.Class("card-body gap-4 text-left")[
                                Ul.Class("space-y-2 text-sm")[
                                    Li[Code.Class("kbd kbd-sm")["rask dev"], " — run with hot reload"],
                                    Li["Edit ", Code.Class("kbd kbd-sm")["HomePage.cs"], " — the page updates as you save"]
                                ],
                                // rask:if islands-any
                                // Each one is an ordinary component: a .cs in Features/Islands/ beside the file that renders it.
                                Div.Class("space-y-3 rounded-box bg-base-200 p-4 text-sm")[
                                    // rask:if islands-react
                                    ReactCounter.Caption("React island"),
                                    // rask:end
                                    // rask:if islands-preact
                                    PreactCounter.Caption("Preact island"),
                                    // rask:end
                                    // rask:if islands-vue
                                    VueCounter.Caption("Vue island"),
                                    // rask:end
                                    // rask:if islands-svelte
                                    SvelteCounter.Caption("Svelte island"),
                                    // rask:end
                                    // rask:if islands-solid
                                    SolidCounter.Caption("Solid island"),
                                    // rask:end
                                    // rask:if islands-lit
                                    LitBadge.Caption("Lit island"),
                                    // rask:end
                                    // rask:if islands-angular
                                    AngularCounter.Caption("Angular island"),
                                    // rask:end
                                    // rask:if islands-blazor
                                    BlazorCounterIsland.Caption("Blazor island"),
                                    // rask:end
                                    P.Class("text-xs text-base-content/60")["Islands live in ", Code.Class("kbd kbd-xs")["Features/Islands/"]]
                                ],
                                // rask:end
                                Div.Class("card-actions justify-end")[
                                    A
                                        .Class("btn btn-primary")
                                        .Href("https://rask.sh/docs/tutorial/00-overview")["Start the tutorial"]
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
