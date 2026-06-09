using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie.Pages;

[Route("/")]
[AllowAnonymous]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "home", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            H1()["Rask cookie + WASM auth sample"],
            P()["A browser-WASM SPA that signs in against its host's ", Code()["/api/login"],
                " (HttpOnly cookie) and hydrates the user from ", Code()["/api/me"], "."],
            P()[A(Href: "/members", Id: "go-members")["Go to the members area →"]]
        ];
}
