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
        Div.Id("members").Class("card shadow-sm mx-auto").Style("max-width:34rem")[
            Div.Class("card-body")[
                Authorize
                    .Authorizing(P.Id("members-authorizing").Class("text-secondary mb-0")["Loading…"])
                    .NotAuthorized(P.Id("members-anon").Class("mb-0")[
                        "Please ", NavLink.Href(Routes.LoginPage())["sign in"], "."])[MemberContent]
            ]
        ];
}

public sealed partial class MemberContent(WasmLoginService login, IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        [
            H1.Id("members-greeting").Class("h3 mb-3")[$"Welcome, {userProvider.Current.Identity?.Name}"],
            Authorize.Roles(["admin"])[
                Div.Id("admin-note").Class("alert alert-warning py-2")["🔑 You have admin access."]],
            Button.Id("logout").OnClickAsync(login.LogoutAsync).Class("btn btn-outline-primary")["Sign out"]
        ];
}
