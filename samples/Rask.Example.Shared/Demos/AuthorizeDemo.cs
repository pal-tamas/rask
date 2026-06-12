namespace Rask.Example.Shared.Demos;

// Declarative auth gating with the headless Authorize component (Authorized / NotAuthorized /
// Authorizing slots). Driven by the same toggleable DemoUserProvider as UserGateDemo — but unlike that
// imperative demo this needs NO manual Changed subscription: the Authorize component subscribes to
// IUserProvider.Changed itself, and its Authorized slot is a delegate handed the current principal, so
// the greeting reads the name with zero plumbing. Nesting an inner Authorize() in the outer's
// NotAuthorized slot yields three distinct states (admin / signed-in / anonymous).
public sealed class AuthorizeDemo : Component
{
    private readonly DemoUserProvider _auth;

    public AuthorizeDemo(DemoUserProvider auth) => _auth = auth;

    protected override RenderResult Render() =>
        Div(Id: "authorize-demo")[
            Div(Class: "d-flex gap-2 mb-3")[
                Button(Class: "btn btn-sm btn-primary", OnClick: () => _auth.SignIn("alice", "user"))[
                    "Sign in as user"],
                Button(Class: "btn btn-sm btn-warning", OnClick: () => _auth.SignIn("rootadmin", "admin"))[
                    "Sign in as admin"],
                Button(Class: "btn btn-sm btn-outline-secondary", OnClick: _auth.SignOut)["Sign out"]
            ],
            // admin → admin slot; any other signed-in user → inner "authorized" slot; anonymous → inner fallback.
            // The Authorized delegates greet the signed-in user by name straight off the principal.
            Authorize(
                ["admin"],
                Authorized: user => Div(Class: "alert alert-warning py-2 mb-0")[
                    $"🔑 Admin-only content — welcome, {user.Identity!.Name}."],
                NotAuthorized: Authorize(
                    Authorized: user => Div(Class: "alert alert-success py-2 mb-0")[
                        $"✅ Signed in as {user.Identity!.Name} — standard access."],
                    NotAuthorized: Div(Class: "alert alert-secondary py-2 mb-0")["🔒 Sign in to see member content."]))
        ];
}
