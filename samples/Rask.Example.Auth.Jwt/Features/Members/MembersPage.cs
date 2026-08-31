using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Core.Routing;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt.Features;

// Component-gated (not a [Authorize] route attribute): the principal is held in the live session, set from
// the JWT held in ProtectedSessionStorage. The Authorize component re-renders when that principal changes.
[AllowAnonymous]
[Route("members")]
public sealed partial class MembersPage : Component
{
    protected override Component? Render() =>
        Div.Id("members").Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 mx-auto").Style("max-width:34rem")[
            Div.Class("p-5")[
                Authorize
                    .NotAuthorized(P.Id("members-anon").Class("mb-0")[
                        "Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])[MemberContent]
            ]
        ];
}

public sealed partial class MemberContent(ProtectedSessionStorage store, SessionUserProvider users, Navigator nav)
    : Component
{
    protected override Component? Render() =>
        [
            H1.Id("members-greeting").Class("text-2xl font-semibold mb-3")[$"Welcome, {users.Current.Identity?.Name}"],
            P.Class("text-slate-500 dark:text-slate-400")["Signed in with a JWT held in ", Code["ProtectedSessionStorage"],
                " — encrypted at rest, decrypted only server-side."],
            Authorize.Roles(["admin"])[
                Div.Id("admin-note").Class("rounded-lg px-4 py-3 text-sm bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200 py-2")["🔑 You have admin access."]],
            Button.Id("logout").OnClickAsync(SignOutAsync).Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-violet-700 ring-violet-300 hover:bg-violet-50 dark:text-violet-300 dark:ring-violet-700 dark:hover:bg-violet-950")["Sign out"]
        ];

    private async Task SignOutAsync()
    {
        await store.DeleteAsync("rask.jwt");
        users.Clear();
        nav.NavigateTo(Routes.LoginPage());
    }
}
