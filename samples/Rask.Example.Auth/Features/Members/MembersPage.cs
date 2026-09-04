using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Features;

// [Authorize] blocks anonymous *deep-links* (full GET → 302 to /login). The headless Authorize
// component gates the *content* — it subscribes to IUserProvider.Changed, so when the post-sign-in
// reconnect re-seeds the principal it re-renders to the Authorized slot. That slot is a delegate
// taking the freshly-authenticated principal, so the greeting reads the name directly — no injected
// IUserProvider and no manual Changed subscription on the page.
[Authorize]
[Route("members")]
public sealed partial class MembersPage : Component
{
    protected override Component? Render() =>
        Div.Id("members").Class("rounded-xl bg-white shadow-sm ring-1 ring-slate-200 dark:bg-slate-800 dark:ring-slate-700 mx-auto").Style("max-width:34rem")[
            Div.Class("p-5")[
                Authorize
                    .Authorizing(P.Id("members-authorizing").Class("text-slate-500 dark:text-slate-400 mb-0")["Signing you in…"])
                    .NotAuthorized(P.Id("members-anon").Class("mb-0")[
                        "Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])
                    .Authorized(user => [
                        H1.Id("members-greeting").Class("text-2xl font-semibold mb-3")[$"Welcome, {user.Identity?.Name}"],
                        MemberContent])
            ]
        ];
}

// The member actions, rendered only once the gate opens. The greeting comes from the Authorized
// delegate above, so this needs no IUserProvider — it injects IAuth purely for sign-out.
public sealed partial class MemberContent(IAuth auth) : Component
{
    protected override Component? Render() =>
        [
            P.Class("text-slate-500 dark:text-slate-400")["This page is gated by ", Code["[Authorize]"],
                " plus the Authorize component."],
            // Role gate: only an admin sees this.
            Authorize.Roles(["admin"])[
                Div.Id("admin-note").Class("rounded-lg px-4 py-3 text-sm bg-amber-50 text-amber-900 dark:bg-amber-950 dark:text-amber-200 py-2")["🔑 You have admin access."]],
            Button
                .Id("logout")
                .OnClickAsync(() => auth.SignOutAsync(returnUrl: "/login"))
                .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-violet-700 ring-violet-300 hover:bg-violet-50 dark:text-violet-300 dark:ring-violet-700 dark:hover:bg-violet-950")["Sign out"]
        ];
}
