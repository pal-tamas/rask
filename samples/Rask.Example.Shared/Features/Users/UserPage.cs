using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("user")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UserPage : Component
{
    protected override RenderResult Head => Title()["User & auth gating — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "User & auth gating",
            "Gate content on the signed-in user — imperatively by injecting IUserProvider and reading .Current (branch in Render() on _auth.Current.Identity?.IsAuthenticated and _auth.Current.IsInRole(...)), or declaratively with the headless Authorize component."),
        H2(Class: "h4 mt-4 mb-3")["Conditional rendering on the current user"],
        CodeSample(
            EmbeddedSource.Read("UserGateDemo.cs"),
            Notes:
            "The principal resolves from the IUserProvider in scope (real apps back it with a cookie/JWT on Server or /api/me on WASM). A component that gates on the user subscribes to the provider's Changed event — the same pattern sidebars use for RouteState — so it re-renders when the principal changes.",
            Result: UserGateDemo()),
        H2(Class: "h4 mt-5 mb-3")["Declarative gating — the Authorize component"],
        CodeSample(
            EmbeddedSource.Read("AuthorizeDemo.cs"),
            Notes:
            "Authorize is the declarative counterpart to gating in Render() on the current user: it picks the Authorized, NotAuthorized, or Authorizing slot off the same IUserProvider. Roles and the authenticated check are synchronous (no flicker); Policy resolves in the background. For whole-page gating use [Authorize] on the page instead. See docs/authentication.md for production flows (cookie/JWT, Identity, Keycloak, Auth0, Cognito, Duende).",
            Result: AuthorizeDemo()),
        H2(Class: "h4 mt-5 mb-3")["Notes"],
        Ul(Class: "text-secondary")[
            Li()[
                "IUserProvider.Current never returns null — with no provider (or signed out) it's an unauthenticated ClaimsPrincipal, so .Current.Identity?.IsAuthenticated is false."],
            Li()[
                "Role checks use the standard ClaimsPrincipal.IsInRole. For route-level gating use [Authorize]/[AllowAnonymous] on the page (RouteAuthorizationGuard) instead of branching in Render()."]
        ]
    ];
}
