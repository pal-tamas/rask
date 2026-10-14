using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Rask.Data.Tests;

/// <summary>Every entry logged through it, with the scopes it was logged in, kept to be asserted on.</summary>
public sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public IReadOnlyList<CapturedLog> Errors => [.. _entries.Where(static e => e.Level >= LogLevel.Error)];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class Logger(string category, CapturedLogs owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scopes = new List<string>();
            owner._scopes.ForEachScope(static (scope, list) => list.Add(scope?.ToString() ?? ""), scopes);
            owner._entries.Enqueue(new CapturedLog(category, logLevel, eventId, exception, scopes));
        }
    }
}

/// <summary>One entry <see cref="CapturedLogs" /> kept.</summary>
public sealed record CapturedLog(
    string Category, LogLevel Level, EventId Id, Exception? Exception, IReadOnlyList<string> Scopes);
