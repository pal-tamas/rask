using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Rask.SqlServer.Tests;

/// <summary>
/// The services <c>UseRaskSqlServer</c> asks for — the configuration, the compatibility-level cache and a logger —
/// and nothing else. The cache defaults to one whose server is never there, so a test that builds options with a
/// made-up connection string asks no real server and gets EF Core's default level, as it did before Rask read one.
/// </summary>
internal sealed class ConfigurationServices(
    IConfiguration configuration,
    SqlServerCompatibilityLevels? levels = null,
    ILoggerFactory? loggers = null) : IServiceProvider
{
    private readonly SqlServerCompatibilityLevels _levels =
        levels ?? new(new FakeLevelReader(_ => throw new TimeoutException("No server in a unit test.")), TimeProvider.System);

    public object? GetService(Type serviceType) =>
        serviceType == typeof(IConfiguration) ? configuration
        : serviceType == typeof(SqlServerCompatibilityLevels) ? _levels
        : serviceType == typeof(ILoggerFactory) ? loggers
        : null;

    /// <summary>Services over <paramref name="settings"/>, with <paramref name="levels"/> as the cache.</summary>
    internal static ConfigurationServices With(
        Dictionary<string, string?> settings,
        SqlServerCompatibilityLevels? levels = null,
        ILoggerFactory? loggers = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), levels, loggers);
}
