using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Rask.SqlServer;

/// <summary>
/// Builds and applies the per-session <c>SET</c> script described by a <see cref="SqlServerOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike PostgreSQL, SQL Server takes no session settings in the connection string, so these must be sent after
/// the connection opens — and on <b>every</b> open, because SqlClient resets a pooled connection's state
/// (<c>sp_reset_connection</c>) when it is handed out again. That costs one round trip per open; the script is a
/// single batch so it is never more than one.
/// </para>
/// <para>
/// The interceptor that sends it only runs on an open EF performs. Rask's own code opens through EF; code that
/// opens the <c>DbConnection</c> directly should do the same (<c>context.Database.OpenConnectionAsync()</c>).
/// </para>
/// </remarks>
internal static class SqlServerSessionSettings
{
    /// <summary>
    /// Renders the <c>SET</c> statements for <paramref name="options"/>, or an empty string when there is nothing
    /// to set.
    /// </summary>
    /// <remarks>
    /// <c>LOCK_TIMEOUT</c> takes integer milliseconds, formatted invariantly so the script is identical under every
    /// culture, and every value in it is a literal this code produced — <c>SET</c> takes no parameters.
    /// </remarks>
    internal static string BuildScript(SqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var script = new StringBuilder();

        if (options.AbortOnError)
        {
            script.Append("SET XACT_ABORT ON;");
        }

        if (options.LockTimeout > TimeSpan.Zero)
        {
            script.Append(CultureInfo.InvariantCulture, $"SET LOCK_TIMEOUT {Milliseconds(options.LockTimeout)};");
        }

        return script.ToString();
    }

    /// <summary>The millisecond count SQL Server is sent for a positive <paramref name="value"/>.</summary>
    /// <remarks>
    /// Rounded UP: <c>SET LOCK_TIMEOUT 0</c> means "do not wait at all", so a sub-millisecond timeout must become
    /// 1, not a statement that fails the instant it meets any lock.
    /// </remarks>
    internal static long Milliseconds(TimeSpan value) => (long)Math.Ceiling(value.TotalMilliseconds);

    /// <summary>The whole-second command timeout SqlClient is sent for a positive <paramref name="value"/>.</summary>
    /// <remarks>Rounded UP: SqlClient reads <c>0</c> as "wait forever".</remarks>
    internal static long Seconds(TimeSpan value) => (long)Math.Ceiling(value.TotalSeconds);

    /// <summary>Applies <paramref name="options"/> to an open connection.</summary>
    internal static void Apply(SqlConnection connection, SqlServerOptions options)
    {
        var script = BuildScript(options);
        if (script.Length == 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = script;
        command.ExecuteNonQuery();
    }

    /// <summary>Applies <paramref name="options"/> to an open connection.</summary>
    internal static async Task ApplyAsync(SqlConnection connection, SqlServerOptions options, CancellationToken cancellationToken)
    {
        var script = BuildScript(options);
        if (script.Length == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
