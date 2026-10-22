namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpAlphanumericExample : Component
{
    private string LicenseKey { get; set; } = "L49R4";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Otp.Bind(() => LicenseKey).Live().Length(10).Alphanumeric.Label("License key")
                .DescriptionTrailing("Enter the license key printed on the installation disc"),
            Ui.Text.Data("testid", "ui-otp-license")[$"Key: {LicenseKey}"]
        ];
}
