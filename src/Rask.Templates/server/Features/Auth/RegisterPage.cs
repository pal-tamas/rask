using Company.RaskServer.Features.Shared;
using Microsoft.AspNetCore.Authorization;
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
[Guest]
[Route("/register")]
public sealed partial class RegisterPage(IAuth auth, FirstRunToken firstRun) : AuthPage
{
    private readonly RegisterModel _model = new();
    private AuthError _error;
    private string? _detail;

    [QueryParam]
    public string? ReturnUrl { get; set; }

    protected override string? PageTitle => "Create an account";

    protected override Component? Content =>
        [
            Title(firstRun.IsPending ? "Claim this app" : "Create an account"),
            _error is AuthError.None ? null : Error("register-error", _detail ?? AuthMessages.For(_error)),
            firstRun.IsPending
                ? Ui.Callout.Secondary.Icon(Ui.IconName.InformationCircle).Role("status").Id("register-first-run")[
                    Ui.CalloutText["This app has no accounts yet, so this one becomes the administrator. "
                                   + "The one-time token is in the startup log."]]
                : null,
            Form.Model(_model).OnSubmit(Submit).Class("flex flex-col gap-6")[
                Ui.Input.Bind(() => _model.DisplayName).Id("display-name").Label("Name"),
                Ui.Input.Bind(() => _model.Email).Id("email").Type(InputType.Email).Label("Email"),
                Ui.Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Label("Password"),
                firstRun.IsPending
                    ? Ui.Input.Bind(() => _model.FirstRunToken).Id("first-run-token").Label("First-run token")
                    : null,
                Ui.Button.Primary.Type(Ui.ButtonType.Submit).Id("register-submit").Class("w-full")[
                    firstRun.IsPending ? "Claim it" : "Create account"]
            ],
            Ui.Text.Class("text-center")["Already have an account? ", Ui.Link.Href(Routes.LoginPage())["Sign in"], "."]
        ];

    private async Task Submit(RegisterModel model)
    {
        // Your own columns are set on the new User before it is saved, in the same insert.
        var result = await auth.Register(
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
