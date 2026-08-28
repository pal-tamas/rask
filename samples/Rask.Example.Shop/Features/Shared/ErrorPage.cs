using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

// The generated Routes class is per-namespace, and this page lives in Features.Shared while the
// home page lives in Features.Home — alias it rather than fully qualifying at the call site.
using HomeRoutes = Rask.Example.Shop.Features.Home.Routes;

namespace Rask.Example.Shop.Features.Shared;

// [AllowAnonymous] because an error page that redirects to /login is worse than the error: if you
// later add a fallback authorization policy, this route must stay reachable.
[AllowAnonymous]
[Route("/error")]
public sealed partial class ErrorPage : Component
{
    protected override Component? HeadAssets => [Title["Something went wrong"]];

    protected override Component? Render() =>
        Main.Class("mx-auto max-w-xl px-4 py-10")[
            Div.Class("rounded-xl border border-slate-200 bg-white p-7 shadow-sm dark:border-slate-700 dark:bg-slate-800")[
                H1.Class("mb-2 text-2xl font-semibold tracking-tight")["Something went wrong"],
                P.Class("mb-4 text-slate-500 dark:text-slate-400")[
                    "The request couldn't be completed. The error has been logged."
                ],
                // The correlation id, and deliberately nothing else. Never render the exception,
                // its message, or a stack trace here — this page is served to whoever hit the
                // error, and the detail already went to ILogger where you can match it by this id.
                Activity.Current?.Id is { Length: > 0 } traceId
                    ? P.Class("mb-4 text-sm text-slate-500 dark:text-slate-400")[
                        "Reference: ",
                        Code.Class("rounded bg-slate-100 px-1.5 py-0.5 dark:bg-slate-700")[traceId]
                    ]
                    : null,
                NavLink
                    .Href(HomeRoutes.HomePage())
                    .Class("text-violet-600 underline underline-offset-2 hover:text-violet-500")[
                    "Back to the app"
                ]
            ]
        ];
}
