using System.Globalization;
using System.Text;

namespace Rask.Site.Features;

// Download.File stages bytes on the active session — served from /_rask/download/{sid}/{token}
// on the server, pulled by JS from the .NET side by token on WASM. It must be called from an event handler,
// so the state and the handler live together in this self-contained component.
public sealed partial class DownloadDemo : Component
{
    private int _reportCount;

    private void DownloadReport()
    {
        _reportCount++;
        var report =
            $"Rask download demo\nGenerated at {DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)}\nCount: {_reportCount}\n";
        Download.File("report.txt", Encoding.UTF8.GetBytes(report), "text/plain");
    }

    protected override Component? Render() =>
        Div[
            Ui.Button.Primary.Icon(Ui.IconName.DocumentText).Id("download-report").OnClick(DownloadReport)["Download report"],
            Div
                .Class("text-sm text-ui-muted mt-2")
                .Data("rask-report-count", "true")[
                $"Generated {_reportCount} time(s)."
            ]
        ];
}
