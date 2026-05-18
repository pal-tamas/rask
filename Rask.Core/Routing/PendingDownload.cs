namespace Rask.Core.Routing;

// Three transport modes — exactly one of Url/Bytes/Token is set per record:
//   * Url   — server path: bytes live in SessionUploadStore, served from /_rask/download/{...}.
//             Render payload carries the URL; the browser fetches it directly.
//   * Bytes — legacy WASM path: bytes inlined as base64 in the JSON render payload. Doubles
//             the payload size (33% base64 inflation + duplicate copy through the .NET ⇄ JS
//             text boundary). Retained for back-compat / test seams.
//   * Token — current WASM path: bytes stay .NET-side, keyed by token in IDownloadSink. The
//             render payload carries only the token; JS calls back into .NET via the
//             PullDownload JSExport to fetch the bytes synchronously when the user-visible
//             <a download> click fires. No base64, no duplicate transport.
public sealed record PendingDownload(
    string Filename,
    string? ContentType,
    string? Url,
    byte[]? Bytes,
    string? Token = null);
