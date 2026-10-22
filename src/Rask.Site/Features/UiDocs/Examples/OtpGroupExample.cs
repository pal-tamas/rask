namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpGroupExample : Component
{
    private string Code { get; set; } = "";

    protected override Component? Render() =>
        Ui.Otp.Bind(() => Code)[
            Ui.OtpGroup[
                Ui.OtpInput,
                Ui.OtpInput,
                Ui.OtpInput,
                Ui.OtpInput,
                Ui.OtpInput,
                Ui.OtpInput
            ]
        ];
}
