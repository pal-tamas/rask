using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Rask.SqlServer;

/// <summary>
/// Builds and applies the per-session <c>SET</c> script described by a <see cref="RaskSqlServerOptions"/>.
/// </summary>
/// <remarks>
/// These are session settings, and SqlClient pools physical connections and resets their state
/// (<c>sp_reset_connection</c>) when one is returned, so the script must run on <b>every</b> open rather
/// than once at startup — the same reason SQLite's pragmas need a connection interceptor.
/// </remarks>
internal static class SqlServerSessionSettings
{
    /// <summary>
    /// Renders the <c>SET</c> statements for <paramref name="options"/>, or an empty string when there is
    /// nothing to set.
    /// </summary>
    /// <remarks>
    /// <c>LOCK_TIMEOUT</c> takes integer milliseconds. Formatting invariantly keeps the script identical
    /// under every culture, and keeps every value in the SQL a literal this code produced — <c>SET</c> does
    /// not accept parameters, so there is nothing else to interpolate.
    /// </remarks>
    internal static string BuildScript(RaskSqlServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var script = new StringBuilder();

        if (options.AbortOnError)
        {
            script.Append("SET XACT_ABORT ON;");
        }

        if (options.LockTimeout > TimeSpan.Zero)
        {
            var milliseconds = (long)Math.Round(options.LockTimeout.TotalMilliseconds, MidpointRounding.AwayFromZero);
            script.Append(CultureInfo.InvariantCulture, $"SET LOCK_TIMEOUT {milliseconds};");
        }

        return script.ToString();
    }

    /// <summary>Applies <paramref name="options"/> to an open connection.</summary>
    internal static void Apply(SqlConnection connection, RaskSqlServerOptions options)
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
    internal static async Task ApplyAsync(
        SqlConnection connection,
        RaskSqlServerOptions options,
        CancellationToken cancellationToken)
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
