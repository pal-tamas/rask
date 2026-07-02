using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie.Features;

[Route("/")]
[AllowAnonymous]
public sealed class HomePage : Component
{
    protected override Component? Render() =>
        Div(Id: "home", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                H1(Class: "h3 card-title mb-3")["Rask cookie + WASM auth sample"],
                P(Class: "card-text text-secondary")[
                    "A browser-WASM SPA that signs in against its host's ", Code()["/api/login"],
                    " (HttpOnly cookie) and hydrates the user from ", Code()["/api/me"], "."],
                NavLink("/members", Id: "go-members", Class: "btn btn-primary")[
                    "Go to the members area →"]
            ]
        ];
}
