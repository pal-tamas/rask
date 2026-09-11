using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Dashboard.Logging;

/// <summary>
/// Feeds <see cref="DashboardLogBuffer" /> from the standard logging pipeline. Registering a provider
/// rather than inventing a channel means the dashboard sees exactly what every other sink sees — the
/// framework's own <c>Rask.Live</c>/<c>Rask.Diff</c>/<c>Rask.Lifecycle</c> categories, the processors'
/// failure logs, and the application's own — with no extra wiring at the call sites.
/// </summary>
/// <remarks>
/// Registered whether or not <see cref="RaskDashboardOptions.CaptureLogs" /> is on, because that setting can come
/// from <c>Rask:Dashboard</c> and is not known until the options are built. With capture off, every logger it
/// hands out is the null logger, which the logging pipeline skips before formatting anything.
/// </remarks>
[ProviderAlias("RaskDashboard")]
internal sealed class DashboardLoggerProvider(DashboardLogBuffer buffer, RaskDashboardOptions options) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        options.CaptureLogs ? new BufferLogger(buffer, categoryName) : NullLogger.Instance;

    public void Dispose()
    {
        // Nothing to release: the buffer is a DI singleton with no unmanaged state.
    }

    private sealed class BufferLogger(DashboardLogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Checked by the logging infrastructure before it formats anything, so an entry below the
        // dashboard's threshold costs a comparison rather than a string.
        public bool IsEnabled(LogLevel logLevel) => buffer.IsEnabled(logLevel);

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

            buffer.Add(logLevel, category, formatter(state, exception), exception);
        }
    }
}
