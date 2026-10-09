using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Authentication;

// A minimal app whose Router exposes pages with every route-gating shape, so the route-guard
// pipeline (Allow / Challenge / Forbid) can be exercised end-to-end over real HTTP.
public sealed partial class RouteGuardTestApp : Component
{
    protected override Component? HeadAssets => Title["route-guard-e2e"];

    protected override Component? Render() => Router;
}

[Route("/e2e/public")]
[AllowAnonymous]
public sealed partial class E2EPublicPage : Component
{
    protected override Component? Render() => Div.Id("public")["public-content"];
}

[Route("/e2e/members")]
[Authorize]
public sealed partial class E2EMembersPage(IUserProvider userProvider) : Component
{
    protected override Component? Render() =>
        Div.Id("members")["members-content for ", Span[userProvider.Current.Identity?.Name ?? "?"]];
}

[Route("/e2e/admin")]
[Authorize(Roles = "admin")]
public sealed partial class E2EAdminPage : Component
{
    protected override Component? Render() => Div.Id("admin")["admin-content"];
}

// A guarded LAYOUT with a page under it, for the two things a page that names itself to its layout must
// not disturb: an unauthorised request never constructs the page, and the signed-in user is already the
// session's when the page's OnMount runs.
[Route("/e2e/vault")]
[Authorize]
public sealed partial class E2EVaultLayout(RouteState route) : Component
{
    protected override Component? Render() =>
        Div.Id("vault")[Span.Id("vault-crumb")[route.Title ?? "nobody"], Outlet];
}

[Route("mine")]
[ParentRoute(typeof(E2EVaultLayout))]
public sealed partial class E2EVaultPage : Component
{
    public static int Constructed;

    private readonly IUserProvider _users;
    private string? _owner;

    public E2EVaultPage(IUserProvider users)
    {
        _users = users;
        Interlocked.Increment(ref Constructed);
    }

    protected override string? PageTitle => _owner is { } owner ? $"vault of {owner}" : null;

    protected override Task OnMount()
    {
        _owner = _users.Current.Identity?.Name;
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["vault-content"];
}

// A layout anyone may open that shows its page to admins only, by wrapping the Outlet in the Authorize
// COMPONENT. A page is constructed where the outlet is rendered, so for everyone else its constructor and
// its hooks never run — which is what makes this a gate and not a curtain.
[Route("/e2e/gated")]
[AllowAnonymous]
public sealed partial class E2EGatedLayout : Component
{
    protected override Component? Render() =>
        Div.Id("gated")[Authorize.Role("admin").NotAuthorized(P["admins only"])[Outlet]];
}

[Route("secret")]
[ParentRoute(typeof(E2EGatedLayout))]
public sealed partial class E2EGatedPage : Component
{
    public static int Constructed;
    public static int Mounted;

    public E2EGatedPage() => Interlocked.Increment(ref Constructed);

    protected override string? PageTitle => "Secret";

    protected override Task OnMount()
    {
        Interlocked.Increment(ref Mounted);
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["secret-content"];
}
