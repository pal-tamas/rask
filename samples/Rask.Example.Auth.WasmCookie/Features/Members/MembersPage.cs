using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmCookie.Features;

// On WASM there's no server route guard — the Authorize component gates the content off the ApiUserProvider
// principal (hydrated from /api/me). The signed-in view is a child component so it reads the fresh principal
// when the gate opens after sign-in.
[AllowAnonymous]
[Route("members")]
public sealed partial class MembersPage : Component
{
    protected override Component? Render() =>
        Div.Id("members").Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 mx-auto").Style("max-width:34rem")[
            Div.Class("p-5")[
                Authorize
                    .Authorizing(P.Id("members-authorizing").Class("text-slate-500 dark:text-slate-400 mb-0")["Loading…"])
                    .NotAuthorized(P.Id("members-anon").Class("mb-0")[
                        "Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])[MemberContent]
            ]
        ];
}

public sealed partial class MemberContent(WasmLoginService login, IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        [
            H1.Id("members-greeting").Class("text-2xl font-semibold mb-3")[$"Welcome, {userProvider.Current.Identity?.Name}"],
            Authorize.Roles(["admin"])[
                Div.Id("admin-note").Class("rounded-lg px-4 py-3 text-sm bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200 py-2")["🔑 You have admin access."]],
            Button.Id("logout").OnClickAsync(login.LogoutAsync).Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-violet-700 ring-violet-300 hover:bg-violet-50 dark:text-violet-300 dark:ring-violet-700 dark:hover:bg-violet-950")["Sign out"]
        ];
}
