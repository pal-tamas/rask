namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpFormExample : Component
{
    private string? _verified;

    public string Code { get; set; } = "";

    protected override Component? Render() =>
        Ui.Card.Class("max-w-sm space-y-8")[
            Div.Class("mx-auto max-w-64 space-y-2")[
                Ui.Heading.Lg.Class("text-center")["Verify your account"],
                Ui.Text.Class("text-center")["Please enter a one-time password from the authenticator app."]
            ],
            Ui.Otp.Bind(() => Code).Length(6).Label("OTP Code").LabelSrOnly().ErrorIcon(false).ErrorClass("text-center").Class("mx-auto"),
            Div.Class("space-y-4")[
                Ui.Button.Primary.Class("w-full").OnClick(() => { _verified = Code; })["Verify"],
                Ui.Button.Class("w-full").Data("testid", "ui-otp-resend").OnClick(() => { Code = ""; })["Resend code"]
            ],
            _verified is null ? null : Ui.Text.Class("text-center")[$"Verifying {_verified}…"]
        ];
}
