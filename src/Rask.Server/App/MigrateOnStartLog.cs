using Microsoft.Extensions.Logging;

namespace Rask;

// Source-generated rather than LogWarning/LogInformation: no argument array is built when the level is off (CA1848/CA1873).
internal static partial class MigrateOnStartLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message =
        "{Context} has no migrations yet, so the database was left as it is. Create the first one with "
        + "`rask db add Init`; the app applies it the next time it starts.")]
    public static partial void NoMigrations(ILogger logger, string context);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s) to the database: {Migrations}")]
    public static partial void Applying(ILogger logger, int count, string migrations);
}
