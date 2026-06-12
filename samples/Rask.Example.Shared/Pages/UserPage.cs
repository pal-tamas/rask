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
            "Gate content on the signed-in user — imperatively by injecting IUserProvider and reading .Current (branch in Render() on _auth.Current.Identity?.IsAuthenticated and _auth.Current.IsInRole(...)), or declaratively with the headless Authorize component."),
        H2(Class: "h4 mt-4 mb-3")["Conditional rendering on the current user"],
        CodeSample(
            """
            // Inject IUserProvider via the constructor and read .Current (a never-null ClaimsPrincipal)
            public sealed class AccountPanel(IUserProvider auth) : Component
            {
                protected override RenderResult Render() =>
                    auth.Current.Identity?.IsAuthenticated == true
                        ? Fragment(
                            P()[$"Signed in as {auth.Current.Identity!.Name}"],
                            auth.Current.IsInRole("admin") ? AdminPanel() : Fragment(),
                            Button(OnClick: auth.SignOut)["Sign out"])
                        : Button(OnClick: () => auth.SignIn("alice", "user"))["Sign in"];

                // re-render when auth changes (the provider raises Changed)
                protected override void OnMount()   => auth.Changed += StateHasChanged;
                protected override void OnUnmount() => auth.Changed -= StateHasChanged;
            }
            """,
            Notes:
            "The principal resolves from the IUserProvider in scope (real apps back it with a cookie/JWT on Server or /api/me on WASM). A component that gates on the user subscribes to the provider's Changed event — the same pattern sidebars use for RouteState — so it re-renders when the principal changes.",
            Result: UserGateDemo()),
        H2(Class: "h4 mt-5 mb-3")["Declarative gating — the Authorize component"],
        CodeSample(
            """
            // Headless: renders exactly one of three slots, no markup of its own.
            // The component subscribes to IUserProvider.Changed itself, so it reacts to sign-in/out.
            // The Authorized slot is a delegate handed the current principal (Blazor's @context.User),
            // so a greeting reads the name with no injected IUserProvider and no manual subscription.
            Authorize(
                Roles: ["admin"],                                  // ANY-of; omit for "any authenticated user"
                Authorized:    user => Div(Class: "alert alert-warning")[$"🔑 Welcome admin, {user.Identity!.Name}."],
                NotAuthorized: Authorize(                        // nest for an authenticated-but-not-admin branch
                    Authorized:    user => Div(Class: "alert alert-success")[$"✅ Signed in as {user.Identity!.Name}."],
                    NotAuthorized: Div(Class: "alert alert-secondary")["🔒 Sign in to see member content."]));

            // Static authorized content (no principal needed) uses the children indexer:
            //   Authorize(Roles: ["admin"])[ AdminPanel() ]
            // Policy gating resolves via IAuthorizationService; the optional Authorizing slot shows
            // until it lands (and while a WASM provider's principal is still loading).
            """,
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
