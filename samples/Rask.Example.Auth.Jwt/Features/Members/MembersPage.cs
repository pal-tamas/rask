using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Rask.Core.Routing;
using Rask.Server.Authentication;

namespace Rask.Example.Auth.Jwt.Features;

// Component-gated (not a [Authorize] route attribute): the principal is held in the live session, set from
// the JWT held in ProtectedSessionStorage. The Authorize component re-renders when that principal changes.
[Route("members")]
[AllowAnonymous]
public sealed partial class MembersPage : Component
{
    protected override Component? Render() =>
        Div(Id: "members", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                Authorize(
                    NotAuthorized: P("members-anon", "mb-0")[
                        "Please ", NavLink(Routes.LoginPage())["sign in"], "."])[MemberContent()]
            ]
        ];
}

public sealed partial class MemberContent(ProtectedSessionStorage store, SessionUserProvider users, Navigator nav)
    : Component
{
    protected override Component? Render() =>
        [
            H1("members-greeting", "h3 mb-3")[$"Welcome, {users.Current.Identity?.Name}"],
            P(Class: "text-secondary")["Signed in with a JWT held in ", Code()["ProtectedSessionStorage"],
                " — encrypted at rest, decrypted only server-side."],
            Authorize(["admin"])[
                Div(Id: "admin-note", Class: "alert alert-warning py-2")["🔑 You have admin access."]],
            Button(Id: "logout", OnClickAsync: SignOutAsync, Class: "btn btn-outline-primary")["Sign out"]
        ];

    private async Task SignOutAsync()
    {
        await store.DeleteAsync("rask.jwt");
        users.Clear();
        nav.NavigateTo(Routes.LoginPage());
    }
}
