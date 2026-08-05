namespace Rask.Cli;

/// <summary>
///     Resolves a path to its canonical form, following symlinks in every segment — not just the last.
/// </summary>
/// <remarks>
///     <para>
///         This exists because of a specific, silent failure. <c>dotnet watch</c> computes an <b>empty</b>
///         Edit-and-Continue delta when the project path it is given traverses a symlink: it reports
///         <c>File updated</c>, updates the document in its Roslyn workspace (<c>Solution after document
///         update: v2</c>), and then says <c>No managed code changes to apply.</c> The edit never reaches
///         the running app, and nothing anywhere reports an error. Hand it the resolved path instead and
///         the same edit produces <c>Sending update batch #0</c> and applies.
///     </para>
///     <para>
///         macOS is where this bites, because <c>/var</c> and <c>/tmp</c> are symlinks into
///         <c>/private</c> — so any project under <see cref="Path.GetTempPath" /> is affected, as is
///         anything a developer keeps under a symlinked working directory. It bit Rask's own watch E2E for
///         months (#536), where it read as "hot reload doesn't work under the test harness".
///     </para>
///     <para>
///         <see cref="Path.GetFullPath(string)" /> does <b>not</b> do this: it normalises <c>.</c>,
///         <c>..</c> and separators but never touches a symlink. <see cref="FileSystemInfo.ResolveLinkTarget" />
///         only resolves the entry it is called on, so a link in an ancestor (<c>/var</c>) is missed unless
///         every segment is walked — which is what this does.
///     </para>
/// </remarks>
internal static class RealPath
{
    /// <summary>
    ///     Returns <paramref name="path" /> with every symlinked segment replaced by its target. Segments
    ///     that do not exist are kept verbatim, so this is safe to call on a path that has not been created
    ///     yet, and on in-memory paths that were never on disk at all.
    /// </summary>
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var segments = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);
            var target = LinkTarget(next);

            // A link target may itself be relative, in which case it resolves against the link's directory.
            current = target is null
                ? next
                : Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
        }

        return current;
    }

    private static string? LinkTarget(string path)
    {
        try
        {
            // Directory.Exists follows the link, so a symlink to a directory lands here and resolves.
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName : null;
        }
        catch (IOException)
        {
            // A cycle or an unreadable link — keep the segment as written rather than failing the command.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
