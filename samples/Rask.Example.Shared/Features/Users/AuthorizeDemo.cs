namespace Rask.Example.Shared.Features;

// Declarative auth gating with the headless Authorize component (Authorized / NotAuthorized /
// Authorizing slots). Driven by the same toggleable DemoUserProvider as UserGateDemo — but unlike that
// imperative demo this needs NO manual Changed subscription: the Authorize component subscribes to
// IUserProvider.Changed itself, and its Authorized slot is a delegate handed the current principal, so
// the greeting reads the name with zero plumbing. Nesting an inner Authorize() in the outer's
// NotAuthorized slot yields three distinct states (admin / signed-in / anonymous).
public sealed partial class AuthorizeDemo : Component
{
    private readonly DemoUserProvider _auth;

    public AuthorizeDemo(DemoUserProvider auth) => _auth = auth;

    protected override Component? Render() =>
        Div.Id("authorize-demo")[
            Div.Class("flex gap-2 flex-wrap items-center mb-3")[
                Button.Type("button").Class(Tw.BtnPrimary).OnClick(() => _auth.SignIn("alice", "user"))[
                    "Sign in as user"],
                Button.Type("button").Class(Tw.BtnWarning).OnClick(() => _auth.SignIn("rootadmin", "admin"))[
                    "Sign in as admin"],
                Button.Type("button").Class(Tw.BtnOutlineSecondary).OnClick(_auth.SignOut)["Sign out"]
            ],
            // admin → admin slot; any other signed-in user → inner "authorized" slot; anonymous → inner fallback.
            // The Authorized delegates greet the signed-in user by name straight off the principal.
            Authorize
                .Roles(["admin"])
                .Authorized(user => Div.Class($"{Tw.AlertWarning} py-2 mb-0")[
                    $"🔑 Admin-only content — welcome, {user.Identity!.Name}."])
                .NotAuthorized(Authorize
                    .Authorized(user => Div.Class($"{Tw.AlertSuccess} py-2 mb-0")[
                        $"✅ Signed in as {user.Identity!.Name} — standard access."])
                    .NotAuthorized(Div.Class($"{Tw.AlertSecondary} py-2 mb-0")["🔒 Sign in to see member content."]))
        ];
}
