using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Jwt.Pages;

[Route("/")]
[AllowAnonymous]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "home", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            H1()["Rask JWT-auth sample"],
            P()["The JWT authenticates the live WebSocket — it rides the upgrade as ", Code()["?access_token="],
                " (set via ", Code()["window.Rask.authToken"], "). The members page gates content with the ",
                Code()["Authorize"], " component over that authenticated socket."],
            P()[A(Href: "/members", Id: "go-members")["Go to the members area →"]]
        ];
}
