using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Pages;

[Route("/")]
[AllowAnonymous]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "home", Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            H1()["Rask cookie-auth sample"],
            P()["A minimal, real cookie login: a protected ", Code()["/members"],
                " page, a ", Code()["/login"], " form, and sign-out — all over Rask's live runtime."],
            P()[A(Href: "/members", Id: "go-members")["Go to the members area →"]]
        ];
}
