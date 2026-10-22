namespace Company.RaskServer.Features.Home;

[Route("/")]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        // A column so the footer sits at the bottom of a short page rather than under the fold.
        Div.Class("flex min-h-screen flex-col bg-white dark:bg-zinc-800")[
            Ui.Header.Container(true).Class("border-b border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900")[
                Ui.Brand.Name("Company.RaskServer"),
                Ui.Spacer,
                Ui.Navbar[Ui.NavbarItem.Href("https://rask.sh/docs")["Docs"]]
            ],
            Main.Class("flex grow flex-col items-center px-4 py-16 text-center")[
                Div.Class("w-full max-w-md")[
                    Ui.Heading.Level(1).Xxl.Class("tracking-tight")["Hello, Rask! 👋"],
                    Ui.Text.Lg.Class("mt-4 mb-8")["Your app is running. What to do next:"],
                    Ui.Card.Class("space-y-6 text-left")[
                        Ul.Class("space-y-3 text-sm text-zinc-800 dark:text-white")[
                            Li[Chip("rask dev"), " — run with hot reload"],
                            Li["Edit ", Chip("HomePage.cs"), " — the page updates as you save"]
                        ],
                        // rask:if islands-any
                        // Each one is an ordinary component: a .cs in Features/Islands/ beside the file that renders it.
                        Div.Class("space-y-3 rounded-lg bg-zinc-800/5 p-4 text-sm dark:bg-white/10")[
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
                            Ui.Text.Sm["Islands live in ", Chip("Features/Islands/")]
                        ],
                        // rask:end
                        Div.Class("flex justify-end")[
                            Ui.Button.Primary.Href("https://rask.sh/docs/tutorial/00-overview")["Start the tutorial"]
                        ]
                    ]
                ]
            ],
            Footer.Class("border-t border-zinc-200 p-4 dark:border-zinc-700")[
                Ui.Text.Class("text-center")["Built with Rask."]
            ]
        ];

    // A file name or a command, set apart from the sentence around it.
    private static Component Chip(string text) =>
        Code.Class("rounded-md bg-zinc-800/5 px-1.5 py-0.5 font-mono text-xs dark:bg-white/10")[text];
}
