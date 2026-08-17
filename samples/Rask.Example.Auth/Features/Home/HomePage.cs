using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Features;

[AllowAnonymous]
public sealed partial class HomePage : Page
{
    protected override string Route => "/";

    protected override Component? Render() =>
        Div.Id("home").Class("card shadow-sm mx-auto").Style("max-width:34rem")[
            Div.Class("card-body")[
                H1.Class("h3 card-title mb-3")["Rask cookie-auth sample"],
                P.Class("card-text text-secondary")[
                    "A minimal, real cookie login: a protected ", Code["/members"],
                    " page, a ", Code["/login"], " form, and sign-out — all over Rask's live runtime."],
                NavLink.Href(Routes.MembersPage()).Id("go-members").Class("btn btn-primary")[
                    "Go to the members area →"]
            ]
        ];
}
