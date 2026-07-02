using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Company.RaskWasm;

[Route("members")]
[AllowAnonymous]
public sealed class MembersPage : Component
{
    protected override Component? Render() =>
        Div(Style: "max-width:32rem;margin:3rem auto;font-family:system-ui")[
            Authorize(
                NotAuthorized: P()["Please ", NavLink(Href: "/login")["sign in"], "."])[MemberContent()]
        ];
}

public sealed class MemberContent(JwtLoginService login, IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        [
            H1()[$"Welcome, {userProvider.Current.Identity?.Name}"],
            Authorize(Roles: ["admin"])[
                Div(Style: "color:#7a5c00")["🔑 You have admin access."]],
            Button(Id: "logout", OnClickAsync: login.LogoutAsync)["Sign out"]
        ];
}
