namespace Rask.Site.Features;

// Auth-gating by injecting IUserProvider and reading .Current — no AuthorizeView component. The demo
// injects the toggleable provider to sign in/out; Render() branches on _auth.Current. It subscribes
// to the provider's Changed event so a sign-in originating anywhere re-renders this component.
public sealed partial class UserGateDemo : Component
{
    private readonly DemoUserProvider _auth;

    public UserGateDemo(DemoUserProvider auth) => _auth = auth;

    protected override void OnMount() => _auth.Changed += StateHasChanged;

    protected override void OnUnmount() => _auth.Changed -= StateHasChanged;

    protected override Component? Render() =>
        Div.Id("user-gate")[
            _auth.Current.Identity?.IsAuthenticated == true
                ? [
                    P["Signed in as ", Strong[_auth.Current.Identity!.Name ?? "?"]],
                    // Role-gated: only an admin sees this panel.
                    _auth.Current.IsInRole("admin")
                        ? UiAlert.Tone(UiTone.Warning).Variant(UiVariant.Soft).Message("🔑 Admin-only panel").Class("py-2")
                        : null,
                    UiButton.Label("Sign out").Variant(UiVariant.Outline).OnClick(_auth.SignOut)]
                : [
                    P.Class("text-ui-muted")["You are signed out."],
                    Div.Class("flex gap-2 flex-wrap items-center")[
                        UiButton.Label("Sign in as user").Tone(UiTone.Primary).OnClick(() => _auth.SignIn("alice", "user")),
                        UiButton.Label("Sign in as admin").Tone(UiTone.Warning)
                            .OnClick(() => _auth.SignIn("rootadmin", "admin"))
                    ]]
        ];
}
