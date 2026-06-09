using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Pages;

[Route("/")]
[AllowAnonymous]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "home", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            H1()["Rask JWT + WASM auth sample"],
            P()["A browser-WASM SPA that signs in against ", Code()["/api/login"],
                ", stores the bearer JWT in localStorage, and sends it as ", Code()["Authorization: Bearer"],
                " on every API call. ", Code()["/api/me"], " validates it server-side."],
            P()[A(Href: "/members", Id: "go-members")["Go to the members area →"]]
        ];
}
