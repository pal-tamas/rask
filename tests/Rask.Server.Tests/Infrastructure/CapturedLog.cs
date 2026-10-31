using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     What one host logged, kept for a test to read: <c>services.AddLogging(l =&gt; l.AddProvider(log))</c>.
/// </summary>
/// <remarks>
///     The host's own log rather than <c>RaskDiagnostics.Sink</c>: a host bridges the sink into its logger factory
///     as it maps its endpoints, so this is where its diagnostics arrive — and it is one host's, not the process's.
/// </remarks>
internal sealed class CapturedLog : ILoggerProvider
{
    public ConcurrentQueue<Entry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

    public void Dispose()
    {
    }

    internal sealed record Entry(string Category, LogLevel Level, string Message, Exception? Exception)
    {
        /// <summary>When it was logged, to line it up with what the test was doing then.</summary>
        public DateTimeOffset At { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class Logger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception));
    }
}
