using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>
/// Feeds <see cref="LogChannel"/> from the standard logging pipeline. Registering a provider rather than
/// inventing a channel means the store sees exactly what every other sink sees — the framework's own
/// <c>Rask.Live</c>/<c>Rask.Diff</c>/<c>Rask.Lifecycle</c> categories, the processors' failure logs, and the
/// application's own — with no extra wiring at the call sites.
/// </summary>
[ProviderAlias("Rask")]
internal sealed class RaskLoggerProvider(
    LogChannel channel,
    RaskLoggingOptions options,
    TimeProvider timeProvider) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new StoreLogger(channel, options, timeProvider, categoryName);

    public void Dispose()
    {
        // Nothing to release: the channel is a DI singleton with no unmanaged state, and the writer owns
        // the drain.
    }

    private sealed class StoreLogger : ILogger
    {
        private readonly LogChannel _channel;
        private readonly RaskLoggingOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly string _category;

        // Resolved once per logger rather than per entry: a category is fixed for the lifetime of the
        // logger, and this check is on the path of every log call in the application.
        private readonly bool _excluded;

        public StoreLogger(
            LogChannel channel,
            RaskLoggingOptions options,
            TimeProvider timeProvider,
            string category)
        {
            _channel = channel;
            _options = options;
            _timeProvider = timeProvider;
            _category = category;
            _excluded = options.IsExcluded(category);
        }

        // Captured when CaptureScopes is on (the default). Returning null here — as this did — is what
        // dropped the request id, the user id and every correlation id an app opened a scope with, which
        // is precisely the state that makes a stored log answer "what else happened on that request?".
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            _options.CaptureScopes ? LogScopes.Push(state) : null;

        // Checked by the logging infrastructure before it formats anything, so an entry below the store's
        // threshold costs a comparison rather than a string.
        public bool IsEnabled(LogLevel logLevel) =>
            !_excluded && logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Id 0 — the store assigns the real one on insert.
            _channel.Write(new LogRecord(
                0,
                _timeProvider.GetUtcNow(),
                logLevel,
                _category,
                eventId.Id,
                formatter(state, exception),
                exception?.ToString(),
                _options.CaptureScopes
                    ? LogScopes.Capture(_options.MaxScopeValues, _options.MaxScopeValueLength)
                    : null));
        }
    }
}
