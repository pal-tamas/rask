namespace Rask.TestFiles;

/// <summary>
///     Walks the repository the way a convention test means to: over the SOURCE tree, pruning build
///     output and vendored dependencies as it descends rather than enumerating them and throwing them
///     away afterwards.
/// </summary>
/// <remarks>
///     <para>
///         The distinction is the whole point of this file. <c>Directory.EnumerateFiles(root, "*",
///         SearchOption.AllDirectories)</c> followed by a <c>.Where(…)</c> is the obvious spelling and it
///         is what two convention tests used, but the filter runs on paths the walk has ALREADY produced,
///         so every one of them still descends into <c>bin/</c>, <c>obj/</c>, <c>node_modules/</c> and
///         <c>.git/</c> in full. Measured on this tree: <b>587,495 files enumerated to examine 3,080</b>
///         — 364k of them under bin/obj, 133k under node_modules. That cost 26 s in one test and 29 s in
///         another, which between them were the two slowest tests in the repository after the demo
///         golden.
///     </para>
///     <para>
///         Pruning at the DIRECTORY makes the same walk proportional to the source tree instead, because
///         a directory that is never entered costs one name comparison rather than everything beneath it.
///     </para>
///     <para>
///         Callers keep their own extension and path filters — this changes only which directories are
///         visited, never how a visited file is judged. Build output being out of scope is not a
///         loosening: generated <c>.cs</c> under <c>obj/</c> is the compiler's copy of source that is
///         already being scanned at its real location, and a convention test that reported it would be
///         naming a file nobody can edit.
///     </para>
/// </remarks>
internal static class RepoFiles
{
    /// <summary>
    ///     Directory names that never hold source, at any depth.
    /// </summary>
    /// <remarks>
    ///     <c>.claude</c> carries the worktrees of OTHER branches (see the repo's worktree workflow); a
    ///     convention test that walked into those would be reporting a different branch's code as though
    ///     it were this one's, which is worse than slow.
    /// </remarks>
    private static readonly string[] Pruned =
    [
        ".git",
        ".claude",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "artifacts",
        "TestResults",
    ];

    /// <summary>
    ///     Every file under <paramref name="root" /> that is not inside a pruned directory.
    /// </summary>
    /// <param name="root">The directory to walk. Usually the repository root.</param>
    /// <param name="alsoPrune">
    ///     Extra directory names to skip, for a caller with tree-specific exclusions of its own.
    /// </param>
    public static IEnumerable<string> EnumerateSourceFiles(string root, params string[] alsoPrune)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentNullException.ThrowIfNull(alsoPrune);

        // Iterative rather than recursive: the depth is unbounded in principle, and an explicit stack
        // keeps a pathological tree from arriving as a StackOverflowException, which cannot be caught.
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            string[] entries;
            try
            {
                entries = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A directory that vanished or refuses to be read is not this test's subject. Skipping it
                // beats failing a convention check on the state of the filesystem.
                continue;
            }

            foreach (var file in entries)
            {
                yield return file;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (!Pruned.Contains(name, StringComparer.Ordinal)
                    && !alsoPrune.Contains(name, StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }
        }
    }
}
