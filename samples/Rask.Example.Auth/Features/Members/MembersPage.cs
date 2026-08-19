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
        Div.Id("members").Class("card shadow-sm mx-auto").Style("max-width:34rem")[
            Div.Class("card-body")[
                Authorize
                    .Authorizing(P.Id("members-authorizing").Class("text-secondary mb-0")["Signing you in…"])
                    .NotAuthorized(P.Id("members-anon").Class("mb-0")[
                        "Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])
                    .Authorized(user => [
                        H1.Id("members-greeting").Class("h3 mb-3")[$"Welcome, {user.Identity?.Name}"],
                        MemberContent])
            ]
        ];
}

// The member actions, rendered only once the gate opens. The greeting now comes from the Authorized
// delegate above, so this no longer needs IUserProvider — it injects IAuthSignIn purely for sign-out.
public sealed partial class MemberContent(IAuthSignIn auth) : Component
{
    protected override Component? Render() =>
        [
            P.Class("text-secondary")["This page is gated by ", Code["[Authorize]"],
                " plus the Authorize component."],
            // Role gate: only an admin sees this.
            Authorize.Roles(["admin"])[
                Div.Id("admin-note").Class("alert alert-warning py-2")["🔑 You have admin access."]],
            Button
                .Id("logout")
                .OnClickAsync(() => auth.SignOutAsync("/login"))
                .Class("btn btn-outline-primary")["Sign out"]
        ];
}
