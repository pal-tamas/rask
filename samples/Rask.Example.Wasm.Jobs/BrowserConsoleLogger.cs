using Microsoft.Extensions.Logging;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Writes log messages to the browser's developer console.
/// </summary>
/// <remarks>
///     A WASM app gets <c>AddLogging()</c> but no provider, so by default every <c>ILogger</c> call goes
///     nowhere — which means a job that fails, a snapshot that hits the storage quota, or a tab that lost
///     the ownership lock all look exactly like nothing happening. A handful of lines is worth it: on this
///     host, <c>Console.WriteLine</c> is the developer console.
/// </remarks>
public sealed class BrowserConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new BrowserConsoleLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class BrowserConsoleLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);

            // Short category: the full namespace-qualified name is most of the line width and none of
            // the information.
            var shortCategory = category[(category.LastIndexOf('.') + 1)..];
            Console.WriteLine($"[{logLevel}] {shortCategory}: {formatter(state, exception)}");

            if (exception is not null)
            {
                Console.WriteLine(exception.ToString());
            }
        }
    }
}
