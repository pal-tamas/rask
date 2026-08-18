using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Jwt.Features;

[AllowAnonymous]
[Route("/")]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        Div.Id("home").Class("card shadow-sm mx-auto").Style("max-width:34rem")[
            Div.Class("card-body")[
                H1.Class("h3 card-title mb-3")["Rask JWT-auth sample"],
                P.Class("card-text text-secondary")[
                    "The JWT authenticates the live WebSocket — it rides the upgrade as ", Code["?access_token="],
                    " (set via ", Code["window.Rask.authToken"], "). The members page gates content with the ",
                    Code["Authorize"], " component over that authenticated socket."],
                NavLink.Href(Routes.MembersPage()).Id("go-members").Class("btn btn-primary")[
                    "Go to the members area →"]
            ]
        ];
}
