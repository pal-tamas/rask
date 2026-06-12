namespace Rask.Example.Shared.Demos;

// Auth-gating by injecting IUserProvider and reading .Current — no AuthorizeView component. The demo
// injects the toggleable provider to sign in/out; Render() branches on _auth.Current. It subscribes
// to the provider's Changed event so a sign-in originating anywhere re-renders this component.
public sealed class UserGateDemo : Component
{
    private readonly DemoUserProvider _auth;

    public UserGateDemo(DemoUserProvider auth) => _auth = auth;

    protected override void OnMount() => _auth.Changed += StateHasChanged;

    protected override void OnUnmount() => _auth.Changed -= StateHasChanged;

    protected override RenderResult Render() =>
        Div(Id: "user-gate")[
            _auth.Current.Identity?.IsAuthenticated == true
                ? Fragment()[
                    P()["Signed in as ", Strong()[_auth.Current.Identity!.Name ?? "?"]],
                    // Role-gated: only an admin sees this panel.
                    _auth.Current.IsInRole("admin")
                        ? Div(Class: "alert alert-warning py-2")["🔑 Admin-only panel"]
                        : (Child)Fragment(),
                    Button(Class: "btn btn-sm btn-outline-secondary", OnClick: _auth.SignOut)["Sign out"]]
                : Fragment()[
                    P(Class: "text-secondary")["You are signed out."],
                    Div(Class: "d-flex gap-2")[
                        Button(Class: "btn btn-sm btn-primary", OnClick: () => _auth.SignIn("alice", "user"))[
                            "Sign in as user"],
                        Button(Class: "btn btn-sm btn-warning", OnClick: () => _auth.SignIn("rootadmin", "admin"))[
                            "Sign in as admin"]
                    ]]
        ];
}
