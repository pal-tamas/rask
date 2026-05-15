namespace Rask.Core.Routing;

public sealed record PendingDownload(
    string Filename,
    string? ContentType,
    string? Url,
    byte[]? Bytes);
