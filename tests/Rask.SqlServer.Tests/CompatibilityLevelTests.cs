using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.SqlServer.Tests;

/// <summary>
/// The compatibility level <c>UseRaskSqlServer</c> hands EF Core: read from the server once per connection string,
/// pinned by <c>Rask:SqlServer:CompatibilityLevel</c>, EF Core's default while the server cannot be reached. The server
/// here is a fake; <c>Rask.Providers.E2E.Tests</c> reads a real one.
/// </summary>
public sealed class CompatibilityLevelTests
{
    private const string App = "Server=db;Database=app;User Id=sa;Password=x;TrustServerCertificate=true";

    private const string Other = "Server=db;Database=other;User Id=sa;Password=x;TrustServerCertificate=true";

    private static readonly int EfDefault = LevelOf(new DbContextOptionsBuilder().UseSqlServer(App).Options);

    [Fact]
    public void The_level_the_server_reports_is_the_one_EF_Core_writes_SQL_for()
    {
        var server = new FakeLevelReader(_ => 170);

        var options = Options(Levels(server));

        Assert.Equal(170, LevelOf(options));
    }

    [Fact]
    public void Rask_SqlServer_CompatibilityLevel_pins_the_level_without_asking_the_server()
    {
        var server = new FakeLevelReader(_ => 170);
        var services = ConfigurationServices.With(
            new() { ["Rask:ConnectionStrings:App"] = App, ["Rask:SqlServer:CompatibilityLevel"] = "150" }, Levels(server));

        var options = new DbContextOptionsBuilder().UseRaskSqlServer(services).Options;

        Assert.Equal(150, LevelOf(options));
        Assert.Equal(0, server.Reads);
    }

    [Fact]
    public void The_options_callback_pins_the_level_too()
    {
        var server = new FakeLevelReader(_ => 170);

        var options = Options(Levels(server), s => s.CompatibilityLevel = 160);

        Assert.Equal(160, LevelOf(options));
        Assert.Equal(0, server.Reads);
    }

    [Fact]
    public void A_connection_string_is_asked_once_however_many_contexts_are_configured_at_once()
    {
        var server = new FakeLevelReader(_ =>
        {
            Thread.Sleep(20);
            return 170;
        });
        var levels = Levels(server);

        var seen = new int[64];
        Parallel.For(0, seen.Length, i => seen[i] = LevelOf(Options(levels)));

        Assert.Equal(1, server.Reads);
        Assert.All(seen, level => Assert.Equal(170, level));
    }

    [Fact]
    public void Each_connection_string_is_asked_for_its_own_database()
    {
        var server = new FakeLevelReader(cs => cs.Contains("Database=app", StringComparison.Ordinal) ? 170 : 130);
        var levels = Levels(server);

        var app = LevelOf(Options(levels));
        var other = LevelOf(Options(levels, connectionString: Other));
        LevelOf(Options(levels));

        Assert.Equal(170, app);
        Assert.Equal(130, other);
        Assert.Equal(2, server.Reads);
    }

    [Fact]
    public void A_server_that_cannot_be_reached_leaves_EF_Core_s_default()
    {
        var server = new FakeLevelReader(_ => throw new TimeoutException("down"));

        var options = Options(Levels(server));

        Assert.Equal(EfDefault, LevelOf(options));
    }

    [Fact]
    public void A_server_that_reports_no_known_level_leaves_EF_Core_s_default_and_is_not_asked_again()
    {
        var server = new FakeLevelReader(_ => null);
        var levels = Levels(server);

        var first = LevelOf(Options(levels));
        var second = LevelOf(Options(levels));

        Assert.Equal(EfDefault, first);
        Assert.Equal(EfDefault, second);
        Assert.Equal(1, server.Reads);
    }

    [Fact]
    public void An_unreachable_server_is_asked_again_after_the_retry_interval_and_not_before()
    {
        var up = false;
        var server = new FakeLevelReader(_ => up ? 170 : throw new InvalidOperationException("down"));
        var clock = new ManualClock();
        var levels = new SqlServerCompatibilityLevels(server, clock);

        var whileDown = LevelOf(Options(levels));
        up = true;
        var tooSoon = LevelOf(Options(levels));
        clock.Advance(SqlServerCompatibilityLevels.RetryAfter);
        var later = LevelOf(Options(levels));

        Assert.Equal(EfDefault, whileDown);
        Assert.Equal(EfDefault, tooSoon);
        Assert.Equal(170, later);
        Assert.Equal(2, server.Reads);
    }

    [Fact]
    public void An_unreachable_server_is_logged_once_as_a_warning_without_the_credentials()
    {
        var server = new FakeLevelReader(_ => throw new TimeoutException("down"));
        var clock = new ManualClock();
        var levels = new SqlServerCompatibilityLevels(server, clock);
        var loggers = new RecordingLoggers();

        Options(levels, loggers: loggers);
        clock.Advance(SqlServerCompatibilityLevels.RetryAfter);
        Options(levels, loggers: loggers);

        var warning = Assert.Single(loggers.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("db/app", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Rask:SqlServer:CompatibilityLevel", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, server.Reads);
    }

    [Fact]
    public void The_level_read_is_logged_once_as_information()
    {
        var levels = Levels(new FakeLevelReader(_ => 160));
        var loggers = new RecordingLoggers();

        Options(levels, loggers: loggers);
        Options(levels, loggers: loggers);

        var read = Assert.Single(loggers.Entries);
        Assert.Equal(LogLevel.Information, read.Level);
        Assert.Contains("160", read.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("17")]
    [InlineData("0")]
    [InlineData("-160")]
    [InlineData("1000")]
    public void A_compatibility_level_that_is_no_SQL_Server_level_is_refused_naming_the_section(string level)
    {
        var services = ConfigurationServices.With(
            new() { ["Rask:ConnectionStrings:App"] = App, ["Rask:SqlServer:CompatibilityLevel"] = level });

        var error = Assert.Throws<OptionsValidationException>(() => new DbContextOptionsBuilder().UseRaskSqlServer(services));

        Assert.Contains("Rask:SqlServer", error.Message, StringComparison.Ordinal);
        Assert.Contains("CompatibilityLevel", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(17, 170)]
    [InlineData(16, 160)]
    [InlineData(15, 150)]
    [InlineData(13, 130)]
    [InlineData(12, null)]
    [InlineData(null, null)]
    public void A_database_not_created_yet_takes_the_level_of_its_server_s_version(int? majorVersion, int? expected)
    {
        var level = SqlServerLevelReader.FromMajorVersion(majorVersion);

        Assert.Equal(expected, level);
    }

    private static SqlServerCompatibilityLevels Levels(FakeLevelReader server) => new(server, TimeProvider.System);

    private static DbContextOptions Options(
        SqlServerCompatibilityLevels levels,
        Action<SqlServerOptions>? configure = null,
        string connectionString = App,
        ILoggerFactory? loggers = null) =>
        new DbContextOptionsBuilder()
            .UseRaskSqlServer(ConfigurationServices.With(new() { ["Rask:ConnectionStrings:App"] = connectionString }, levels, loggers), configure)
            .Options;

#pragma warning disable EF1001 // EF Core keeps the level on its internal options extension; there is no public reader.
    private static int LevelOf(DbContextOptions options) =>
        options.FindExtension<SqlServerOptionsExtension>()!.SqlServerCompatibilityLevel;
#pragma warning restore EF1001
}
