using System.Globalization;

namespace Rask.SQLite.Snapshots;

/// <summary>How a snapshot of a database is named — <c>{stem}-{yyyyMMdd-HHmmssfff}.db</c> — and whether a file is one.</summary>
internal static class SnapshotNames
{
    private const string Timestamp = "yyyyMMdd-HHmmssfff";

    /// <summary>The name of the snapshot of the database <paramref name="stem" /> taken at <paramref name="at" />.</summary>
    internal static string For(string stem, DateTimeOffset at) =>
        $"{stem}-{at.ToString(Timestamp, CultureInfo.InvariantCulture)}.db";

    /// <summary>
    ///     Whether <paramref name="fileName" /> is a snapshot of the database <paramref name="stem" /> — and not of another
    ///     whose name starts the same: <c>app-audit-…</c> is <c>app-audit</c>'s, never <c>app</c>'s to prune.
    /// </summary>
    internal static bool IsOf(string stem, string fileName)
    {
        var prefix = stem + "-";
        return fileName.Length == prefix.Length + Timestamp.Length + ".db".Length
            && fileName.StartsWith(prefix, StringComparison.Ordinal)
            && fileName.EndsWith(".db", StringComparison.Ordinal)
            && DateTime.TryParseExact(
                fileName.AsSpan(prefix.Length, Timestamp.Length), Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }
}
