using System.Globalization;
using System.Text;
using MySql.Data.MySqlClient;

namespace Rask.MySql;

/// <summary>
/// Builds and applies the per-session <c>SET</c> statement described by a <see cref="MySqlOptions"/>.
/// </summary>
/// <remarks>
/// The driver takes no session variables in the connection string, so these are sent after the connection opens —
/// on every open, because a new physical connection starts from the server's defaults. One statement, so one round
/// trip. The interceptor that sends it runs only on an open EF performs; code that opens the <c>DbConnection</c>
/// directly should open through <c>context.Database.OpenConnectionAsync()</c> instead.
/// </remarks>
internal static class MySqlSessionSettings
{
    /// <summary>The largest <c>innodb_lock_wait_timeout</c> MySQL accepts, in seconds.</summary>
    internal const long MaxLockWaitSeconds = 1_073_741_824;

    /// <summary>The largest command timeout MySql.Data honours, in seconds; it cuts a longer one down to this.</summary>
    internal const int MaxCommandTimeoutSeconds = int.MaxValue / 1000;

    /// <summary>
    /// Renders the <c>SET SESSION</c> statement for <paramref name="options"/>, or an empty string when every setting
    /// is left to the server.
    /// </summary>
    /// <remarks>
    /// Every value is an integer this code produced, formatted invariantly — <c>SET</c> takes no parameters, so
    /// there is nothing else to interpolate.
    /// </remarks>
    internal static string BuildScript(MySqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assignments = new List<string>(2);

        if (options.LockTimeout > TimeSpan.Zero)
        {
            assignments.Add(string.Create(CultureInfo.InvariantCulture, $"SESSION innodb_lock_wait_timeout = {Seconds(options.LockTimeout)}"));
        }

        if (options.StatementTimeout > TimeSpan.Zero)
        {
            assignments.Add(string.Create(CultureInfo.InvariantCulture, $"SESSION max_execution_time = {Milliseconds(options.StatementTimeout)}"));
        }

        return assignments.Count == 0
            ? string.Empty
            : new StringBuilder("SET ").AppendJoin(", ", assignments).Append(';').ToString();
    }

    /// <summary>The whole-second count sent for a positive <paramref name="value"/>.</summary>
    /// <remarks>Rounded UP: MySQL takes no fraction here, and a short timeout must not round down to nothing.</remarks>
    internal static long Seconds(TimeSpan value) => (long)Math.Ceiling(value.TotalSeconds);

    /// <summary>The millisecond count sent for a positive <paramref name="value"/>.</summary>
    /// <remarks>Rounded UP: <c>max_execution_time = 0</c> means "no limit".</remarks>
    internal static long Milliseconds(TimeSpan value) => (long)Math.Ceiling(value.TotalMilliseconds);

    /// <summary>Applies <paramref name="options"/> to an open connection.</summary>
    internal static void Apply(MySqlConnection connection, MySqlOptions options)
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
    internal static async Task ApplyAsync(MySqlConnection connection, MySqlOptions options, CancellationToken cancellationToken)
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
