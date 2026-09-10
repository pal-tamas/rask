namespace Rask.Site.Features;

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
                UiButton.Label("Sign in as user").Tone(UiTone.Primary).OnClick(() => _auth.SignIn("alice", "user")),
                UiButton.Label("Sign in as admin").Tone(UiTone.Warning).OnClick(() => _auth.SignIn("rootadmin", "admin")),
                UiButton.Label("Sign out").Variant(UiVariant.Outline).OnClick(_auth.SignOut)
            ],
            // admin → admin slot; any other signed-in user → inner "authorized" slot; anonymous → inner fallback.
            // The Authorized delegates greet the signed-in user by name straight off the principal.
            Authorize
                .Roles(["admin"])
                .Authorized(user => UiAlert.Tone(UiTone.Warning).Variant(UiVariant.Soft).Message($"🔑 Admin-only content — welcome, {user.Identity!.Name}.").Class("py-2 mb-0"))
                .NotAuthorized(Authorize
                    .Authorized(user => UiAlert.Tone(UiTone.Success).Variant(UiVariant.Soft).Message($"✅ Signed in as {user.Identity!.Name} — standard access.").Class("py-2 mb-0"))
                    .NotAuthorized(UiAlert.Variant(UiVariant.Soft).Message("🔒 Sign in to see member content.").Class("py-2 mb-0")))
        ];
}
