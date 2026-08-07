using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

// The generated Routes class is per-namespace, and this page lives in Features.Shared while the
// home page lives in Features.Home — alias it rather than fully qualifying at the call site.
using HomeRoutes = Rask.Example.Shop.Features.Home.Routes;

namespace Rask.Example.Shop.Features.Shared;

// [AllowAnonymous] because an error page that redirects to /login is worse than the error: if you
// later add a fallback authorization policy, this route must stay reachable.
[Route("/error")]
[AllowAnonymous]
public sealed partial class ErrorPage : Component
{
    protected override Component? Head => [Title()["Something went wrong"]];

    protected override Component? Render() =>
        Div(Class: "mx-auto my-5", Style: "max-width:540px")[
            BsCard(Class: "shadow-sm")[
                BsCardBody()[
                    BsCardTitle()["Something went wrong"],
                    BsCardText(Class: "text-body-secondary")[
                        "The request couldn't be completed. The error has been logged."
                    ],
                    // The correlation id, and deliberately nothing else. Never render the
                    // exception, its message, or a stack trace here — this page is served to
                    // whoever hit the error, and the detail already went to ILogger where you can
                    // match it by this id.
                    Activity.Current?.Id is { Length: > 0 } traceId
                        ? P(Class: "mb-3 small text-body-secondary")[
                            "Reference: ",
                            Code()[traceId]
                        ]
                        : null,
                    NavLink(HomeRoutes.HomePage(), Class: "btn btn-primary")["Back to the app"]
                ]
            ]
        ];
}
