using System.Globalization;
using System.Text;
using Npgsql;

namespace Rask.Postgres;

/// <summary>
/// Turns a <see cref="PostgresOptions"/>' timeouts into startup parameters on the connection string.
/// </summary>
/// <remarks>
/// <para>
/// A startup parameter — the connection string's <c>Options</c>, sent as <c>-c name=value</c> when the physical
/// connection is established — becomes the session's <em>default</em>, and that matters twice. Npgsql's pool
/// resets a returned connection with <c>DISCARD ALL</c>, which restores every setting to its default: a
/// <c>SET</c> issued after opening would be undone, but a startup parameter is what it is restored <em>to</em>.
/// And there is nothing to send per open: a <c>SET</c> on every open would cost a round trip on every query EF
/// runs, and would never reach code that opens the <c>DbConnection</c> itself rather than through EF.
/// </para>
/// <para>
/// A pooler in transaction mode (PgBouncer) rejects startup parameters it does not know unless
/// <c>options</c> is in its <c>ignore_startup_parameters</c>. Behind one, set the timeouts to
/// <see cref="TimeSpan.Zero"/> and configure them on the database role instead.
/// </para>
/// </remarks>
internal static class PostgresSessionSettings
{
    /// <summary>
    /// Renders the <c>-c name=value</c> startup parameters for <paramref name="options"/>, or an empty string
    /// when every timeout is left to the server.
    /// </summary>
    /// <remarks>
    /// Values are integer milliseconds, which is what PostgreSQL's <c>*_timeout</c> settings take as a bare
    /// number, formatted invariantly so the parameter is identical under every culture.
    /// </remarks>
    internal static string BuildStartupOptions(PostgresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parameters = new StringBuilder();
        Append(parameters, "statement_timeout", options.StatementTimeout);
        Append(parameters, "lock_timeout", options.LockTimeout);
        Append(parameters, "idle_in_transaction_session_timeout", options.IdleInTransactionSessionTimeout);
        return parameters.ToString();

        static void Append(StringBuilder parameters, string setting, TimeSpan value)
        {
            // Zero leaves the setting to the server, so a server-level or role-level value wins.
            if (value <= TimeSpan.Zero)
            {
                return;
            }

            if (parameters.Length > 0)
            {
                parameters.Append(' ');
            }

            parameters.Append(CultureInfo.InvariantCulture, $"-c {setting}={Milliseconds(value)}");
        }
    }

    /// <summary>
    /// The millisecond count PostgreSQL is sent for a positive <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Rounded UP: PostgreSQL reads <c>0</c> as "no limit", so a sub-millisecond timeout must become 1, not the
    /// opposite of what was asked for.
    /// </remarks>
    internal static long Milliseconds(TimeSpan value) => (long)Math.Ceiling(value.TotalMilliseconds);

    /// <summary>
    /// Returns <paramref name="connectionString"/> with <paramref name="options"/>' startup parameters added to
    /// its <c>Options</c>, or unchanged when there are none.
    /// </summary>
    /// <remarks>
    /// Rask's parameters go first and the string's own after them: PostgreSQL applies a repeated <c>-c</c> in
    /// order, so a value an environment wrote into its connection string deliberately still wins.
    /// </remarks>
    internal static string Apply(string connectionString, PostgresOptions options)
    {
        var startup = BuildStartupOptions(options);
        if (startup.Length == 0)
        {
            return connectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? startup : $"{startup} {builder.Options}";
        return builder.ConnectionString;
    }
}
