namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpSeparatorExample : Component
{
    private string Code { get; set; } = "";

    protected override Component? Render() =>
        Ui.Otp.Bind(() => Code)[
            Ui.OtpInput,
            Ui.OtpInput,
            Ui.OtpInput,
            Ui.OtpSeparator,
            Ui.OtpInput,
            Ui.OtpInput,
            Ui.OtpInput
        ];
}
