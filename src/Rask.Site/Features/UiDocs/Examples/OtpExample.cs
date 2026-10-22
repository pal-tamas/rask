using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class OtpExample : Component
{
    private string Code { get; set; } = "";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Otp.Bind(() => Code).Live().Length(6),
            Ui.Text.Data("testid", "ui-otp-state")[
                Code.Length == 6 ? $"Code complete: {Code}." : $"{Code.Length.ToString(CultureInfo.InvariantCulture)} of 6 entered."
            ]
        ];
}
