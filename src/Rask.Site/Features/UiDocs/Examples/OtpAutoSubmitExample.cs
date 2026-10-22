namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpAutoSubmitExample : Component
{
    private string? _verified;

    private string Code { get; set; } = "";

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-8")[
            Div.Class("mx-auto max-w-64 space-y-2")[
                Ui.Heading.Lg.Class("text-center")["Verify your account"],
                Ui.Text.Class("text-center")["Please enter a one-time password from the authenticator app."]
            ],
            Div.Class("space-y-6")[
                // Where Flux's submit="auto" submits the form: the handler runs once the last cell is filled.
                Ui.Otp.Bind(() => Code).Length(6).OnComplete(code => { _verified = code; }).Class("mx-auto")
            ],
            Ui.Text.Class("text-center").Data("testid", "ui-otp-verified")[
                _verified is null ? "Fill every cell to verify." : $"Verifying {_verified}…"
            ]
        ];
}
