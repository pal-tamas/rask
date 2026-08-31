using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;
using Rask.Server.Diagnostics;

namespace Rask.Server.Tests.Diagnostics;

// Exercises the bridge's per-event Emit directly rather than installing it as the process-global
// RaskDiagnostics.Sink, so these tests never race host-based tests that drive the same global seam.
[Collection("DiagnosticsSink")]
public class RaskServerDiagnosticsBridgeTests
{
    // RaskLogLevel is internal, so it can't be a public test-method parameter — pass its numeric
    // value and cast inside. LogLevel is public.
    [Theory]
    [InlineData((int)RaskLogLevel.Error, LogLevel.Error)]
    [InlineData((int)RaskLogLevel.Warning, LogLevel.Warning)]
    [InlineData((int)RaskLogLevel.Information, LogLevel.Information)]
    public void Emit_RoutesToILogger_WithMappedLevelCategoryAndException(int raskLevel, LogLevel expected)
    {
        var provider = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));

        var boom = new InvalidOperationException("boom");
        RaskServerDiagnostics.Emit(
            factory, new RaskDiagnosticEvent((RaskLogLevel)raskLevel, "Rask.Test.Bridge", "bridged fault", boom));

        var entry = Assert.Single(provider.Entries, e => e.Category == "Rask.Test.Bridge");
        Assert.Equal(expected, entry.Level);
        Assert.Same(boom, entry.Exception);
        Assert.Contains("bridged fault", entry.Message);
    }

    [Fact]
    public void Emit_WhenLoggingThrows_Swallows_SoADiagnosticNeverBecomesAFault()
    {
        // A disposed factory / misbehaving provider must not escape into the framework's catch blocks.
        var ex = Record.Exception(() =>
            RaskServerDiagnostics.Emit(
                new ThrowingLoggerFactory(),
                new RaskDiagnosticEvent(RaskLogLevel.Error, "Rask.Test", "ignored", null)));

        Assert.Null(ex);
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

    private sealed class ThrowingLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => throw new ObjectDisposedException("factory");

        public void Dispose()
        {
        }
    }
}
