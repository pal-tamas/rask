using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Features;

[Route("/")]
[AllowAnonymous]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        Div.Id("home").Class("card shadow-sm mx-auto").Style("max-width:34rem")[
            Div.Class("card-body")[
                H1.Class("h3 card-title mb-3")["Rask JWT + WASM auth sample"],
                P.Class("card-text text-secondary")[
                    "A browser-WASM SPA that signs in against ", Code["/api/login"],
                    ", stores the bearer JWT in localStorage, and sends it as ", Code["Authorization: Bearer"],
                    " on every API call. ", Code["/api/me"], " validates it server-side."],
                NavLink.Href(Routes.MembersPage()).Id("go-members").Class("btn btn-primary")[
                    "Go to the members area →"]
            ]
        ];
}
