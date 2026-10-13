using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core.Authentication;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     An app that signs users in and registers nothing a live page can ask about them is told so as it starts: its
///     open pages cannot learn that a sign-in ended.
/// </summary>
[Collection("DiagnosticsSink")]
public sealed class NothingRevalidatesWarningTests
{
    [Fact]
    public void A_host_with_sign_in_and_no_revalidator_is_warned_once_and_told_what_to_register()
    {
        var said = Starting(static services => services.AddAuthentication("TestCookie").AddCookie("TestCookie"));

        var warning = Assert.Single(said);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Rask.Live", warning.Category);
        Assert.Contains("docs/authentication.md#what-ties-a-live-page-to-its-user", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_registers_a_revalidator_is_told_nothing()
    {
        var said = Starting(static services =>
        {
            services.AddAuthentication("TestCookie").AddCookie("TestCookie");
            services.AddSingleton<ISessionRevalidator, Anybody>();
        });

        Assert.Empty(said);
    }

    [Fact]
    public void A_host_with_no_sign_in_is_told_nothing()
    {
        var said = Starting(static _ => { });

        Assert.Empty(said);
    }

    // What the host's own log was told about revalidation as it started.
    private static Said[] Starting(Action<IServiceCollection> configure)
    {
        var log = new Log();

        using var host = RaskTestHost.Create<SignInTestApp>(services =>
        {
            services.AddLogging(logging => logging.AddProvider(log));
            configure(services);
        });

        return [.. log.Entries.Where(static e => e.Message.Contains("ISessionRevalidator", StringComparison.Ordinal))];
    }

    private sealed record Said(string Category, LogLevel Level, string Message);

    private sealed class Log : ILoggerProvider
    {
        public ConcurrentQueue<Said> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<Said> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue(new Said(category, logLevel, formatter(state, exception)));
        }
    }

    private sealed class Anybody : ISessionRevalidator
    {
        public ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
            new(principal);
    }
}
