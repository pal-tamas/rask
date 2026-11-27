namespace Rask.Core.Routing;

// Two transport modes — exactly one of Url/Token is set per record:
//   * Url   — server path: bytes live in SessionDownloadStore, served from /_rask/download/{token}.
//             Render payload carries the URL; the browser fetches it directly.
//   * Token — WASM path: bytes stay .NET-side, keyed by token in IDownloadSink. The render
//             payload carries only the token; JS calls back into .NET via the PullDownload
//             JSExport to fetch the bytes synchronously when the user-visible <a download>
//             click fires. No base64, no duplicate transport.
public sealed record PendingDownload(
    string Filename,
    string? ContentType,
    string? Url,
    string? Token = null);
