namespace Rask.Cli.Commands;

/// <summary>A label value read off a <c>docker ps --format</c> column.</summary>
internal static class DockerLabel
{
    /// <summary>
    ///     The label's value, trimmed, or empty when the container does not carry it: docker prints
    ///     <c>&lt;no value&gt;</c> for a label that is missing.
    /// </summary>
    internal static string Value(string column) =>
        string.Equals(column, "<no value>", StringComparison.Ordinal) ? string.Empty : column.Trim();
}
