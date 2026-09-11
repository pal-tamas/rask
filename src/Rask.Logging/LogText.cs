namespace Rask.Logging;

/// <summary>Makes captured text storable on every database the log can live in.</summary>
internal static class LogText
{
    /// <summary>
    /// Replaces NUL with U+FFFD.
    /// </summary>
    /// <remarks>
    /// PostgreSQL refuses a text value holding NUL, and the refusal is not transient: it fails the whole INSERT batch
    /// the line arrived in, every other line of that flush with it. A NUL gets into a log easily — a user-supplied
    /// value, a buffer's <c>ToString()</c>. Both stores apply this, so a line reads back the same wherever the log is
    /// kept. Scope state needs nothing: its JSON encoding already escapes NUL.
    /// </remarks>
    internal static string WithoutNul(string value) =>
        value.Contains('\0', StringComparison.Ordinal) ? value.Replace('\0', '�') : value;
}
