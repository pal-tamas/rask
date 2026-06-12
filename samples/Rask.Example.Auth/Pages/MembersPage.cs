using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.Pages;

// [Authorize] blocks anonymous *deep-links* (full GET → 302 to /login). The headless Authorize
// component gates the *content* — it subscribes to IUserProvider.Changed, so when the post-sign-in
// reconnect re-seeds the principal it re-renders to the Authorized slot. The signed-in view lives in
// its own component (MemberContent) so it first renders only once the gate opens, reading the
// freshly-authenticated principal — no manual Changed subscription needed on the page.
[Route("members")]
[Authorize]
public sealed class MembersPage : Component
{
    protected override RenderResult Render() =>
        Div(Id: "members", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                Authorize(
                    Authorizing: P("members-authorizing", "text-secondary mb-0")["Signing you in…"],
                    NotAuthorized: P("members-anon", "mb-0")[
                        "Please ", NavLink("/login")["sign in"], "."],
                    Authorized: MemberContent())
            ]
        ];
}

// Rendered only by the Authorize component's Authorized slot, so its Render reads the current
// (authenticated) principal. Injects IUserProvider for the principal and IAuthSignIn for sign-out.
public sealed class MemberContent(IUserProvider userProvider, IAuthSignIn auth) : Component
{
    protected override RenderResult Render() =>
        Fragment()[
            H1("members-greeting", "h3 mb-3")[$"Welcome, {userProvider.Current.Identity?.Name}"],
            P(Class: "text-secondary")["This page is gated by ", Code()["[Authorize]"],
                " plus the Authorize component."],
            // Role gate: only an admin sees this.
            Authorize(
                ["admin"],
                Authorized: Div(Id: "admin-note", Class: "alert alert-warning py-2")["🔑 You have admin access."],
                NotAuthorized: (Child)Fragment()),
            Button(Id: "logout", OnClickAsync: () => auth.SignOutAsync("/login"),
                Class: "btn btn-outline-primary")["Sign out"]
        ];
}
