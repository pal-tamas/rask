namespace Rask.SQLite.Litestream;

/// <summary>
/// Resolves the litestream executable to run. When the package's build target has dropped a
/// <c>litestream</c> binary next to the app (the default), a bare executable name resolves to that
/// bundled copy; otherwise it is left as-is for a normal <c>PATH</c> lookup. An absolute path, a path
/// with a directory separator, or a name that already exists as a file is always used verbatim.
/// </summary>
internal static class LitestreamExecutableResolver
{
    public static string Resolve(string configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        // An explicit path (rooted or containing a separator) wins as given. A bare name is intentionally
        // NOT treated as a working-directory file here: the runner resolves a bare name via PATH, so we
        // must hand it an absolute path (below) for a bundled binary to actually run.
        if (Path.IsPathRooted(configured)
            || configured.Contains('/', StringComparison.Ordinal)
            || configured.Contains(Path.DirectorySeparatorChar))
        {
            return configured;
        }

        // Prefer a binary bundled next to the app by the build target.
        var baseDirectory = AppContext.BaseDirectory;
        foreach (var candidateName in new[] { configured, configured + ".exe" })
        {
            var candidate = Path.Combine(baseDirectory, candidateName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Nothing bundled — fall back to a PATH lookup on the bare name.
        return configured;
    }
}
