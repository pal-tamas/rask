using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Authentication;

// A minimal app whose Router exposes pages with every route-gating shape, so the route-guard
// pipeline (Allow / Challenge / Forbid) can be exercised end-to-end over real HTTP.
public sealed class RouteGuardTestApp : Component
{
    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head()[Title()["route-guard-e2e"]],
            Body()[Router()]
        ]
    ];
}

[Route("/e2e/public")]
[AllowAnonymous]
public sealed class E2EPublicPage : Component
{
    protected override Component? Render() => Div(Id: "public")["public-content"];
}

[Route("/e2e/members")]
[Authorize]
public sealed class E2EMembersPage(IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        Div(Id: "members")["members-content for ", Span()[userProvider.Current.Identity?.Name ?? "?"]];
}

[Route("/e2e/admin")]
[Authorize(Roles = "admin")]
public sealed class E2EAdminPage : Component
{
    protected override Component? Render() => Div(Id: "admin")["admin-content"];
}
