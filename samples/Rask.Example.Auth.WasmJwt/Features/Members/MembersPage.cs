using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Example.Auth.WasmJwt.Features;

[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override Component? Render() =>
        Div(Id: "members", Class: "card shadow-sm mx-auto", Style: "max-width:34rem")[
            Div(Class: "card-body")[
                Authorize(
                    Authorizing: P("members-authorizing", "text-secondary mb-0")["Loading…"],
                    NotAuthorized: P("members-anon", "mb-0")[
                        "Please ", NavLink(Routes.LoginPage())["sign in"], "."])[MemberContent()]
            ]
        ];
}

public sealed class MemberContent(JwtLoginService login, IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        [
            H1("members-greeting", "h3 mb-3")[$"Welcome, {userProvider.Current.Identity?.Name}"],
            Authorize(["admin"])[
                Div(Id: "admin-note", Class: "alert alert-warning py-2")["🔑 You have admin access."]],
            Button(Id: "logout", OnClickAsync: login.LogoutAsync, Class: "btn btn-outline-primary")["Sign out"]
        ];
}
