using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;
using Rask.Server.Diagnostics;

namespace Rask.Server.Tests.Diagnostics;

public class RaskServerDiagnosticsBridgeTests
{
    [Fact]
    public void Install_RoutesFrameworkDiagnostics_ToILogger()
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var previousSink = RaskDiagnostics.Sink;
        try
        {
            RaskServerDiagnostics.Install(factory);

            var boom = new InvalidOperationException("boom");
            RaskDiagnostics.Report(RaskLogLevel.Error, "Rask.Test.Bridge", "bridged fault", boom);

            var entry = Assert.Single(provider.Entries, e => e.Category == "Rask.Test.Bridge");
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(boom, entry.Exception);
            Assert.Contains("bridged fault", entry.Message);
        }
        finally
        {
            RaskDiagnostics.Sink = previousSink;
        }
    }

    [Fact]
    public void Install_Null_LeavesSinkUnchanged()
    {
        var previousSink = RaskDiagnostics.Sink;
        RaskServerDiagnostics.Install(null);
        Assert.Same(previousSink, RaskDiagnostics.Sink);
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, ConcurrentBag<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
