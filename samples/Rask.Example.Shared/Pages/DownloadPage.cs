using System.Globalization;
using System.Text;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("download")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DownloadPage(Navigator nav) : Component
{
    private int _reportCount;

    protected override RenderResult Head => Title()["File download — Rask"];

    private void DownloadReport()
    {
        _reportCount++;
        var report =
            $"Rask download demo\nGenerated at {DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)}\nCount: {_reportCount}\n";
        nav.Download("report.txt", Encoding.UTF8.GetBytes(report), "text/plain");
    }

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "File download",
            "Navigator.Download stages bytes (or a stream) on the active session. On the server they're served from /_rask/download/{sid}/{token}; on WASM they're handed to JS as a base64 payload. The component code is the same."),
        H2(Class: "h4 mt-4 mb-3")["Generated report"],
        CodeSample(
            """
            public sealed class DownloadPage(Navigator nav) : Component
            {
                private int _count;

                private void DownloadReport()
                {
                    _count++;
                    var report = $"Generated at {DateTimeOffset.UtcNow}\nCount: {_count}";
                    nav.Download("report.txt",
                                 Encoding.UTF8.GetBytes(report),
                                 "text/plain");
                }

                protected override RenderResult Render() =>
                    Button(OnClick: DownloadReport)["Download report"];
            }
            """,
            Notes:
            "Navigator.Download must be called from an event handler — outside that scope it throws, because there's no live render round-trip to attach the download to. The handler can do other state changes too (here, bump a counter); both ship in the same render.",
            Result: RenderReport())
    ];

    private Component RenderReport() =>
        Div()[
            Button(
                "button",
                Class: "btn btn-primary",
                Id: "download-report",
                OnClick: DownloadReport)[
                I(Class: "bi bi-file-earmark-text me-2"),
                "Download report"
            ],
            Div(Class: "small text-secondary mt-2",
                Data: new Dictionary<string, string?> { ["rask-report-count"] = "true" })[
                $"Generated {_reportCount} time(s)."
            ]
        ];
}
