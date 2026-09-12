using Microsoft.Extensions.Logging;

namespace Rask.DevTools.Fixture.Wasm;

/// <summary>
///     Writes every log entry to the browser console, so a hand run of this fixture sees the framework's diagnostics.
/// </summary>
/// <remarks>
///     The WASM host registers logging with no provider, and forwards every framework diagnostic into it, so without one
///     a fault reported during a dispatch reaches nobody (#1096). Remove this once the host writes to the console itself.
/// </remarks>
internal sealed class FixtureConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            Console.WriteLine($"[{category}] {logLevel}: {formatter(state, exception)}");
            if (exception is not null)
            {
                Console.WriteLine(exception.ToString());
            }
        }
    }
}
