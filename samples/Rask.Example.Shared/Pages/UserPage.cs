using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("user")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class UserPage : Component
{
    protected override RenderResult Head => Title()["User & auth gating — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "User & auth gating",
            "Gate content on the signed-in user — imperatively with the built-in Component.User (branch in Render() on User.Identity?.IsAuthenticated and User.IsInRole(...)), or declaratively with the headless Authorize component."),
        H2(Class: "h4 mt-4 mb-3")["Conditional rendering on User"],
        CodeSample(
            """
            // Component.User comes from the registered IUserProvider
            protected override RenderResult Render() =>
                User.Identity?.IsAuthenticated == true
                    ? Fragment(
                        P()[$"Signed in as {User.Identity!.Name}"],
                        User.IsInRole("admin") ? AdminPanel() : Fragment(),
                        Button(OnClick: _auth.SignOut)["Sign out"])
                    : Button(OnClick: () => _auth.SignIn("alice", "user"))["Sign in"];

            // re-render when auth changes (the provider raises Changed)
            protected override void OnMount()   => _auth.Changed += StateHasChanged;
            protected override void OnUnmount() => _auth.Changed -= StateHasChanged;
            """,
            Notes:
            "User resolves from the IUserProvider in scope (real apps back it with a cookie/JWT on Server or /api/me on WASM). A component that gates on User subscribes to the provider's Changed event — the same pattern sidebars use for RouteState — so it re-renders when the principal changes.",
            Result: UserGateDemo()),
        H2(Class: "h4 mt-5 mb-3")["Declarative gating — the Authorize component"],
        CodeSample(
            """
            // Headless: renders exactly one of three slots, no markup of its own.
            // The component subscribes to IUserProvider.Changed itself, so it reacts to sign-in/out.
            Authorize(
                Roles: ["admin"],                                  // ANY-of; omit for "any authenticated user"
                Authorized:    Div(Class: "alert alert-warning")["🔑 Admin-only content."],
                NotAuthorized: Authorize(                        // nest for an authenticated-but-not-admin branch
                    Authorized:    Div(Class: "alert alert-success")["✅ Signed in — standard access."],
                    NotAuthorized: Div(Class: "alert alert-secondary")["🔒 Sign in to see member content."]));

            // Shorthand — children are the Authorized branch:
            //   Authorize(Roles: ["admin"])[ AdminPanel() ]
            // Policy gating resolves via IAuthorizationService; the optional Authorizing slot shows
            // until it lands (and while a WASM provider's principal is still loading).
            """,
            Notes:
            "Authorize is the declarative counterpart to gating in Render() on User: it picks the Authorized, NotAuthorized, or Authorizing slot off the same Component.User. Roles and the authenticated check are synchronous (no flicker); Policy resolves in the background. For whole-page gating use [Authorize] on the page instead. See docs/authentication.md for production flows (cookie/JWT, Identity, Keycloak, Auth0, Cognito, Duende).",
            Result: AuthorizeDemo()),
        H2(Class: "h4 mt-5 mb-3")["Notes"],
        Ul(Class: "text-secondary")[
            Li()[
                "Component.User never returns null — with no provider (or signed out) it's an unauthenticated ClaimsPrincipal, so User.Identity?.IsAuthenticated is false."],
            Li()[
                "Role checks use the standard ClaimsPrincipal.IsInRole. For route-level gating use [Authorize]/[AllowAnonymous] on the page (RouteAuthorizationGuard) instead of branching in Render()."]
        ]
    ];
}
