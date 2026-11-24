using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.SqlServer;

/// <summary>
/// The compatibility level each connection string's database runs at, read from the server once and kept for the
/// life of the process.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseRaskSqlServer</c> runs on every options build — once per scope for <c>AddDbContext</c>, once per context for
/// one that configures itself — so the read must not. Once known, a level is a dictionary lookup with no lock; the lock
/// is per connection string and held only by the read, so two contexts starting together send one probe.
/// </para>
/// <para>
/// A server that cannot be reached is not remembered as "no level": the context gets EF Core's default, the failure
/// is logged once, and the first options build after <see cref="RetryAfter"/> asks again. Until then nothing is sent,
/// so a database that is down costs a request no extra wait.
/// </para>
/// </remarks>
/// <param name="reader">What asks the server.</param>
/// <param name="clock">The clock the retry interval is measured on.</param>
internal sealed partial class SqlServerCompatibilityLevels(ISqlServerLevelReader reader, TimeProvider clock)
{
    /// <summary>How long a failed read stands before the server is asked again.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Probe> _probes = new(StringComparer.Ordinal);

    /// <summary>The one every context in the process shares, reading real servers.</summary>
    internal static SqlServerCompatibilityLevels Shared { get; } = new(new SqlServerLevelReader(), TimeProvider.System);

    /// <summary>The cache <paramref name="services"/> registers — a test's — or else <see cref="Shared"/>.</summary>
    internal static SqlServerCompatibilityLevels From(IServiceProvider services) =>
        services.GetService<SqlServerCompatibilityLevels>() ?? Shared;

    /// <summary>
    /// The level of the database <paramref name="connectionString"/> names, or <see langword="null"/> — EF Core's
    /// default — when the server reports none Rask can map or cannot be reached right now.
    /// </summary>
    internal int? For(string connectionString, IServiceProvider services)
    {
        var probe = _probes.GetOrAdd(connectionString, static _ => new Probe());
        if (probe.Known is { } known)
        {
            return known.Level;
        }

        lock (probe.Gate)
        {
            return probe.Known is { } readMeanwhile ? readMeanwhile.Level : ReadUnderLock(probe, connectionString, services);
        }
    }

    private int? ReadUnderLock(Probe probe, string connectionString, IServiceProvider services)
    {
        var now = clock.GetUtcNow();
        if (now < probe.RetryAt)
        {
            return null;
        }

        var logger = Logger(services);
        try
        {
            var level = reader.Read(connectionString);
            probe.Known = new Reading(level);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var where = Where(connectionString);
                var reported = level?.ToString(CultureInfo.InvariantCulture) ?? "none known, EF Core's default";
                LogRead(logger, where, reported);
            }

            return level;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            probe.RetryAt = now + RetryAfter;
            if (!probe.Warned && logger.IsEnabled(LogLevel.Warning))
            {
                probe.Warned = true;
                var where = Where(connectionString);
                LogUnreachable(logger, ex, where, RetryAfter);
            }

            return null;
        }
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetService<ILoggerFactory>()?.CreateLogger("Rask.SqlServer") ?? NullLogger.Instance;

    // Server and database only: the rest of a connection string is credentials.
    private static string Where(string connectionString)
    {
        try
        {
            var parsed = new SqlConnectionStringBuilder(connectionString);
            return $"{parsed.DataSource}/{parsed.InitialCatalog}";
        }
        catch (ArgumentException)
        {
            return "the configured server";
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL Server compatibility level of {Database}: {Level}.")]
    private static partial void LogRead(ILogger logger, string database, string level);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read the SQL Server compatibility level of {Database}, so EF Core's default is used; asking "
            + "again after {RetryAfter}. Set Rask:SqlServer:CompatibilityLevel to choose it without asking the server.")]
    private static partial void LogUnreachable(ILogger logger, Exception exception, string database, TimeSpan retryAfter);

    /// <summary>What is known about one connection string's database.</summary>
    private sealed class Probe
    {
        public readonly Lock Gate = new();

        // Written under the lock, read without it; once set it never changes.
        public volatile Reading? Known;

        // Both only touched under the lock.
        public DateTimeOffset RetryAt;
        public bool Warned;
    }

    private sealed record Reading(int? Level);
}
