using Microsoft.AspNetCore.Authorization;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Jwt.Features;

[AllowAnonymous]
[Route("/")]
public sealed partial class HomePage : Component
{
    protected override Component? Render() =>
        Div.Id("home").Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 mx-auto").Style("max-width:34rem")[
            Div.Class("p-5")[
                H1.Class("mb-1 text-lg font-semibold text-2xl mb-3")["Rask JWT-auth sample"],
                P.Class("text-slate-500 dark:text-slate-400")[
                    "The JWT authenticates the live WebSocket — it rides the upgrade as ", Code["?access_token="],
                    " (set via ", Code["window.Rask.authToken"], "). The members page gates content with the ",
                    Code["Authorize"], " component over that authenticated socket."],
                NavLink.Href(Routes.MembersPage()).Id("go-members").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500")[
                    "Go to the members area →"]
            ]
        ];
}
