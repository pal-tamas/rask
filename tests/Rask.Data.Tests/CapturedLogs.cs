using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Rask.Data.Tests;

/// <summary>Every entry logged through it, kept to be asserted on.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, EventId Id)> _entries = new();

    public IReadOnlyList<(string Category, LogLevel Level, EventId Id)> Entries => [.. _entries];

    public IReadOnlyList<(string Category, LogLevel Level, EventId Id)> Errors =>
        [.. _entries.Where(static e => e.Level >= LogLevel.Error)];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, EventId)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, logLevel, eventId));
    }
}
