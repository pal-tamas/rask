namespace Rask.SQLite.Litestream;

/// <summary>
/// Builds the argument lists passed to the <c>litestream</c> executable. Pure and side-effect free so
/// the exact invocation is unit-testable without the binary present.
/// </summary>
public static class LitestreamCommand
{
    /// <summary>
    /// The <c>restore</c> arguments for the startup restore: pull the latest snapshot+WAL for the database
    /// from its replica, into the database's own path. Uses <c>-if-replica-exists</c> so a first-ever boot
    /// (no replica yet) is a no-op rather than an error.
    /// </summary>
    public static IReadOnlyList<string> Restore(LitestreamOptions options) =>
        Restore(options, outputPath: null, ifReplicaExists: true);

    /// <summary>
    /// The <c>restore</c> arguments, with the output path and the missing-replica behaviour spelled out.
    /// </summary>
    /// <param name="options">The configured database, replica and/or config file.</param>
    /// <param name="outputPath">
    /// Where to write the restored database (<c>-o</c>), or <see langword="null"/> to restore over
    /// <see cref="LitestreamOptions.DatabasePath"/>. Verification restores <b>elsewhere</b>, so it always
    /// passes a path — including in <c>-config</c> mode, where the positional argument still selects which
    /// of the configured databases to pull.
    /// </param>
    /// <param name="ifReplicaExists">
    /// Whether to pass <c>-if-replica-exists</c>, which turns "there is no replica at all" into a silent
    /// success. Right for the startup restore (a first-ever boot has nothing to pull); <b>wrong</b> for
    /// verification, where a replica that isn't there is precisely the failure being looked for.
    /// </param>
    public static IReadOnlyList<string> Restore(LitestreamOptions options, string? outputPath, bool ifReplicaExists)
    {
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string> { "restore" };

        if (ifReplicaExists)
        {
            args.Add("-if-replica-exists");
        }

        if (!string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            args.Add("-config");
            args.Add(options.ConfigPath);

            if (outputPath is not null)
            {
                args.Add("-o");
                args.Add(outputPath);
            }

            args.Add(RequireDatabasePath(options));
        }
        else
        {
            args.Add("-o");
            args.Add(outputPath ?? RequireDatabasePath(options));
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
