namespace Rask.Example.Shared.Features;

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
                        ? Div.Class($"{Ui.AlertWarning} py-2")["🔑 Admin-only panel"]
                        : null,
                    Button.Type("button").Class(Ui.BtnOutlineSecondary).OnClick(_auth.SignOut)["Sign out"]]
                : [
                    P.Class("text-slate-500 dark:text-slate-400")["You are signed out."],
                    Div.Class("flex gap-2 flex-wrap items-center")[
                        Button.Type("button").Class(Ui.BtnPrimary).OnClick(() => _auth.SignIn("alice", "user"))[
                            "Sign in as user"],
                        Button.Type("button").Class(Ui.BtnWarning)
                            .OnClick(() => _auth.SignIn("rootadmin", "admin"))[
                            "Sign in as admin"]
                    ]]
        ];
}
