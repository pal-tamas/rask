using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("download")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class DownloadPage : Component
{
    protected override Component? Head => Title()["File download — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "File download",
            "Navigator.Download stages bytes (or a stream) on the active session. On the server they're served from /_rask/download/{sid}/{token}; on WASM they're handed to JS as a base64 payload. The component code is the same."),
        H2(Class: "h4 mt-4 mb-3")["Generated report"],
        CodeSample(
            ["DownloadDemo.cs"],
            Notes:
            "Navigator.Download must be called from an event handler — outside that scope it throws, because there's no live render round-trip to attach the download to. The handler can do other state changes too (here, bump a counter); both ship in the same render.",
            Result: DownloadDemo())
    ];
}
