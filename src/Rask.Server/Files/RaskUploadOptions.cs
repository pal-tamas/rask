namespace Rask.Server.Files;

public sealed class RaskUploadOptions
{
    public long MaxFileSize { get; set; } = 50 * 1024 * 1024;

    public int MaxFilesPerRequest { get; set; } = 16;
}
