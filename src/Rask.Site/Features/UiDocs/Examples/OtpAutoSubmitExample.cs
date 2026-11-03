namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpAutoSubmitExample : Component
{
    private readonly Verification _verification = new();
    private string? _verified;

    protected override Component? Render() =>
        Form.Model(_verification).OnSubmit(_ => { _verified = _verification.Code; }).Class("max-w-sm space-y-8")[
            Div.Class("mx-auto max-w-64 space-y-2")[
                Ui.Heading.Lg.Class("text-center")["Verify your account"],
                Ui.Text.Class("text-center")["Please enter a one-time password from the authenticator app."]
            ],
            Div.Class("space-y-6")[
                Ui.Otp.Bind(() => _verification.Code).Length(6).Submit(Ui.OtpSubmit.Auto).Class("mx-auto")
            ],
            Ui.Text.Class("text-center").Data("testid", "ui-otp-verified")[
                _verified is null ? "Fill every cell to verify." : $"Verifying {_verified}…"
            ]
        ];

    public sealed class Verification
    {
        public string Code { get; set; } = "";
    }
}
