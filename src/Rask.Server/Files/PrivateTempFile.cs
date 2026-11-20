namespace Rask.Server.Files;

/// <summary>
///     A staged upload or download in the system's temp directory, readable by this process's user alone: the umask would
///     leave it <c>0644</c> in a <c>/tmp</c> every local account can list and read.
/// </summary>
internal static class PrivateTempFile
{
    internal static FileStream Create(string path, bool asynchronous)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = asynchronous ? FileOptions.Asynchronous : FileOptions.None,
        };

        // Windows has no mode bits; its temp directory is the user's own already.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return new FileStream(path, options);
    }
}
