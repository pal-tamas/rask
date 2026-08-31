using Rask.Core.Routing;

namespace Rask.Example.Shop.Features.Home;

[Route("/")]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        Main.Class("mx-auto max-w-xl px-4 py-10")[
            Div.Class("rounded-xl border border-slate-200 bg-white p-7 shadow-sm dark:border-slate-700 dark:bg-slate-800")[
                H1.Class("mb-2 text-2xl font-semibold tracking-tight")["Hello, Rask! 👋"],
                P.Class("mb-4 text-slate-500 dark:text-slate-400")["Your app is ready. What to do next:"],
                Ul.Class("mb-4 list-disc space-y-1 pl-5")[
                    Li[Code.Class("rounded bg-violet-100 px-1.5 py-0.5 text-violet-700")["rask dev"], " — run with hot reload"],
                    Li[Code.Class("rounded bg-violet-100 px-1.5 py-0.5 text-violet-700")["rask db add Init"], " — create the database"],
                    Li[A.Class("text-violet-600 underline underline-offset-2 hover:text-violet-500").Href("https://github.com/pal-tamas/rask/blob/main/docs/tutorial/02-first-feature.md")["Build your first feature"]]
                ],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Edit this page in ",
                    Code.Class("rounded bg-slate-100 px-1.5 py-0.5 dark:bg-slate-700")["HomePage.cs"],
                    " — Tailwind rebuilds the stylesheet from it on the next build."
                ]
            ]
        ];
}
