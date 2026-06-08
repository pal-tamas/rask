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
                "Gate content on the signed-in user with the built-in Component.User — no AuthorizeView component. Branch in Render() on User.Identity?.IsAuthenticated and User.IsInRole(...)."),
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
            H2(Class: "h4 mt-5 mb-3")["Notes"],
            Ul(Class: "text-secondary")[
                Li()["Component.User never returns null — with no provider (or signed out) it's an unauthenticated ClaimsPrincipal, so User.Identity?.IsAuthenticated is false."],
                Li()["Role checks use the standard ClaimsPrincipal.IsInRole. For route-level gating use [Authorize]/[AllowAnonymous] on the page (RouteAuthorizationGuard) instead of branching in Render()."]
            ]
        ];
}
