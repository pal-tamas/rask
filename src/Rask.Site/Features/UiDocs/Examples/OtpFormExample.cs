namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpFormExample : Component
{
    private string _code = "";
    private string? _verified;

    protected override Component? Render() =>
        Ui.Card.Class("max-w-sm space-y-8")[
            Div.Class("mx-auto max-w-64 space-y-2")[
                Ui.Heading.Lg.Class("text-center")["Verify your account"],
                Ui.Text.Class("text-center")["Please enter a one-time password from the authenticator app."]
            ],
            Ui.Otp.Value(_code).OnChange(code => { _code = code; }).Length(6).Label("OTP Code").Class("mx-auto"),
            Div.Class("space-y-4")[
                Ui.Button.Primary.Class("w-full").OnClick(() => { _verified = _code; })["Verify"],
                Ui.Button.Class("w-full").Data("testid", "ui-otp-resend").OnClick(() => { _code = ""; })["Resend code"]
            ],
            _verified is null ? null : Ui.Text.Class("text-center")[$"Verifying {_verified}…"]
        ];
}
