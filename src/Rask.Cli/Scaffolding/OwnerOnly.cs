namespace Rask.Cli.Scaffolding;

/// <summary>Narrows a file or directory to its owner, for the few things the CLI writes that are secrets.</summary>
/// <remarks>
///     A no-op on Windows, whose per-user profile ACLs already keep another account out and which has no
///     mode bits to set. Elsewhere a filesystem that carries no permissions — a mounted share, a container
///     overlay — refuses, and that is swallowed: failing a scaffold or a dev loop over file metadata would
///     cost more than the narrowing buys.
/// </remarks>
internal static class OwnerOnly
{
    /// <summary>Read and write for the owner, nothing for anybody else.</summary>
    internal const UnixFileMode File = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>A directory the owner can list and enter, nobody else.</summary>
    internal const UnixFileMode Directory = File | UnixFileMode.UserExecute;

    internal static void Restrict(string path, UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            System.IO.File.SetUnixFileMode(path, mode);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
