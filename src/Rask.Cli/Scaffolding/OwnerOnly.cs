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

    /// <summary>
    /// Write <paramref name="content"/> to a file only its owner can read. Created 0600 rather than written and then
    /// narrowed, so the secret is never readable by anybody else, not even for the instant in between. An EXISTING file
    /// keeps the mode it was created with, so it is narrowed afterwards as well.
    /// </summary>
    internal static void WriteText(string path, string content)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = File;
        }

        using (var writer = new StreamWriter(path, options))
        {
            writer.Write(content);
        }

        Restrict(path, File);
    }

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
            // Best effort: a file system without Unix modes keeps its defaults, which is all this can offer.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort, as above: a path this user does not own keeps the mode its owner gave it.
        }
    }
}
