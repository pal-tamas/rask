namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpPrivateExample : Component
{
    private string Pin { get; set; } = "";

    protected override Component? Render() =>
        Ui.Otp.Bind(() => Pin).Length(4).Private().Label("PIN Code");
}
