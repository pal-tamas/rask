using System.Globalization;
using System.Text;
using Npgsql;

namespace Rask.Postgres;

/// <summary>
/// Builds and applies the per-session <c>SET</c> script described by a <see cref="RaskPostgresOptions"/>.
/// </summary>
/// <remarks>
/// These are session settings, not server settings: Npgsql pools physical connections and resets their
/// state (<c>DISCARD ALL</c>) when one is returned to the pool, so the script must run on <b>every</b> open
/// rather than once at startup. That is the same reason SQLite's pragmas need a connection interceptor.
/// </remarks>
internal static class PostgresSessionSettings
{
    /// <summary>
    /// Renders the <c>SET</c> statements for <paramref name="options"/>, or an empty string when every
    /// timeout is disabled.
    /// </summary>
    /// <remarks>
    /// Timeouts are emitted as integer milliseconds, which is what PostgreSQL's <c>*_timeout</c> GUCs take
    /// when given a bare number. Formatting them invariantly (rather than as <c>'30s'</c> or a decimal)
    /// keeps the script identical under every culture, and keeps every value in the SQL a literal integer
    /// this code produced — <c>SET</c> does not accept parameters, so there is nothing else to interpolate.
    /// </remarks>
    internal static string BuildScript(RaskPostgresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var script = new StringBuilder();
        Append(script, "statement_timeout", options.StatementTimeout);
        Append(script, "lock_timeout", options.LockTimeout);
        Append(script, "idle_in_transaction_session_timeout", options.IdleInTransactionSessionTimeout);
        return script.ToString();

        static void Append(StringBuilder script, string setting, TimeSpan value)
        {
            // Zero is PostgreSQL's own "disabled", and it is also the server default — so rather than
            // emitting `SET x = 0` we leave the setting alone, which lets a server-level or role-level
            // configuration win instead of being overwritten with the same value.
            if (value <= TimeSpan.Zero)
            {
                return;
            }

            var milliseconds = (long)Math.Round(value.TotalMilliseconds, MidpointRounding.AwayFromZero);
            script.Append(CultureInfo.InvariantCulture, $"SET {setting} = {milliseconds};");
        }
    }

    /// <summary>Applies <paramref name="options"/> to an open connection.</summary>
    internal static void Apply(NpgsqlConnection connection, RaskPostgresOptions options)
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
        NpgsqlConnection connection,
        RaskPostgresOptions options,
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
