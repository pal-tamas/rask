using System.Globalization;
using System.Text;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

// Navigator.Download stages bytes on the active session — served from /_rask/download/{sid}/{token}
// on the server, handed to JS as a base64 payload on WASM. It must be called from an event handler,
// so the state and the handler live together in this self-contained component.
public sealed partial class DownloadDemo(Navigator nav) : Component
{
    private int _reportCount;

    private void DownloadReport()
    {
        _reportCount++;
        var report =
            $"Rask download demo\nGenerated at {DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)}\nCount: {_reportCount}\n";
        nav.Download("report.txt", Encoding.UTF8.GetBytes(report), "text/plain");
    }

    protected override Component? Render() =>
        Div[
            Button.Type("button").Class(Tw.BtnPrimary).Id("download-report").OnClick(DownloadReport)[
                UiIcon.Name(UiIconName.Document).Class("me-2"),
                "Download report"
            ],
            Div
                .Class("text-sm text-ui-muted mt-2")
                .Data(new Dictionary<string, string?> { ["rask-report-count"] = "true" })[
                $"Generated {_reportCount} time(s)."
            ]
        ];
}
