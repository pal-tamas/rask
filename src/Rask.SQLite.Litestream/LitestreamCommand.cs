namespace Rask.SQLite.Litestream;

/// <summary>
/// Builds the argument lists passed to the <c>litestream</c> executable. Pure and side-effect free so
/// the exact invocation is unit-testable without the binary present.
/// </summary>
public static class LitestreamCommand
{
    /// <summary>
    /// The <c>restore</c> arguments: pull the latest snapshot+WAL for the database from its replica.
    /// Uses <c>-if-replica-exists</c> so a first-ever boot (no replica yet) is a no-op rather than an error.
    /// </summary>
    public static IReadOnlyList<string> Restore(LitestreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string> { "restore", "-if-replica-exists" };

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            args.Add("-config");
            args.Add(options.ConfigPath);
            args.Add(RequireDatabasePath(options));
        }
        else
        {
            args.Add("-o");
            args.Add(RequireDatabasePath(options));
            args.Add(RequireReplicaUrl(options));
        }

        return args;
    }

    /// <summary>
    /// The <c>replicate</c> arguments: continuously stream the WAL to the replica for the life of the
    /// process. Uses <c>-config</c> when a config file is supplied, otherwise the positional db + replica.
    /// </summary>
    public static IReadOnlyList<string> Replicate(LitestreamOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string> { "replicate" };

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            args.Add("-config");
            args.Add(options.ConfigPath);
        }
        else
        {
            args.Add(RequireDatabasePath(options));
            args.Add(RequireReplicaUrl(options));
        }

        return args;
    }

    private static string RequireDatabasePath(LitestreamOptions options) =>
        !string.IsNullOrWhiteSpace(options.DatabasePath)
            ? options.DatabasePath
            : throw new InvalidOperationException($"{nameof(LitestreamOptions.DatabasePath)} is required.");

    private static string RequireReplicaUrl(LitestreamOptions options) =>
        !string.IsNullOrWhiteSpace(options.ReplicaUrl)
            ? options.ReplicaUrl
            : throw new InvalidOperationException($"{nameof(LitestreamOptions.ReplicaUrl)} is required.");
}
