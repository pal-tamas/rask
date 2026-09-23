using Company.RaskServer.Features.Shared;
using Microsoft.AspNetCore.Authorization;
using Rask.Core.Authentication;
using Rask.Core.Routing;
using Rask.Wire;

namespace Company.RaskServer.Features.Auth;

public sealed class RegisterModel
{
    public string DisplayName { get; set; } = "";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    // Needed only while this app has no accounts yet.
    public string FirstRunToken { get; set; } = "";
}

// The first-run token field appears only while the app is unclaimed, which is the only time it is asked for.
[AllowAnonymous]
[Route("/register")]
public sealed partial class RegisterPage(IAuth auth, FirstRunToken firstRun) : AuthPage
{
    private readonly RegisterModel _model = new();
    private AuthError _error;
    private string? _detail;

    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override Component? HeadAssets => Title["Create an account"];

    protected override Component? Content =>
        [
            H1.Class("text-2xl font-bold")[firstRun.IsPending ? "Claim this app" : "Create an account"],
            _error is AuthError.None ? null : Error("register-error", _detail ?? AuthMessages.For(_error)),
            firstRun.IsPending
                ? Div.Class("alert alert-info").Role("status").Id("register-first-run")[
                    Span["This app has no accounts yet, so this one becomes the administrator. "
                         + "The one-time token is in the startup log."]]
                : null,
            Form.Model(_model).OnSubmit(SubmitAsync)[
                Field("display-name", "Name", Input.Bind(() => _model.DisplayName).Id("display-name").Class("input w-full")),
                Field("email", "Email", Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Class("input w-full")),
                Field("password", "Password", Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("input w-full")),
                firstRun.IsPending
                    ? Field("first-run-token", "First-run token", Input.Bind(() => _model.FirstRunToken).Id("first-run-token").Class("input w-full"))
                    : null,
                Div.Class("card-actions mt-2")[
                    Button.Type("submit").Id("register-submit").Class("btn btn-primary btn-block")[
                        firstRun.IsPending ? "Claim it" : "Create account"]
                ]
            ],
            P.Class("text-sm opacity-70")[
                "Already have an account? ",
                NavLink.Href(Routes.LoginPage()).Class("link link-primary")["Sign in"],
                "."]
        ];

    private async Task SubmitAsync(RegisterModel model)
    {
        // Your own columns are set on the new User before it is saved, in the same insert.
        var result = await auth.RegisterAsync(
            model.Email,
            model.Password,
            (User user) => user.Rename(model.DisplayName),
            ReturnUrl,
            string.IsNullOrWhiteSpace(model.FirstRunToken) ? null : model.FirstRunToken);

        _error = result.Error;

        // A policy failure carries the reason, such as the minimum length.
        _detail = result.Message;
    }
}
