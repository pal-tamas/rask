using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Rask.Wasm.Diagnostics;

/// <summary>
///     Writes log entries to the browser console. Registered by the WASM host only when the app registered no logging
///     provider of its own.
/// </summary>
/// <remarks>
///     <para>
///         The host forwards every framework diagnostic into <c>ILogger</c> (<see cref="RaskWasmDiagnostics" />), and a
///         logger factory with no provider writes nowhere. So on an app that added none, every warning and error the
///         framework reported was discarded, and the browser console showed only the boot lines (#1096).
///     </para>
///     <para>
///         Written against <see cref="Console" /> because the .NET browser runtime already maps it to the page's
///         console: standard output to <c>console.log</c>, standard error to <c>console.error</c>.
///         <c>Microsoft.Extensions.Logging.Console</c> is not an option — it runs a background thread, which a
///         single-threaded browser runtime does not have. Errors go to standard error so they show up as errors;
///         everything else goes to standard output, so a warning does not paint the console red.
///     </para>
/// </remarks>
internal sealed class BrowserConsoleLoggerProvider(TextWriter output, TextWriter error) : ILoggerProvider
{
    public BrowserConsoleLoggerProvider() : this(Console.Out, Console.Error)
    {
    }

    /// <summary>Registers the console provider unless the app has registered a logging provider of its own.</summary>
    /// <remarks>
    ///     An app that chose its providers keeps exactly those. Adding this beside them would print every entry twice
    ///     to a console the app may already be writing to.
    /// </remarks>
    public static void AddIfNoneRegistered(IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(ILoggerProvider)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, BrowserConsoleLoggerProvider>());
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, output, error);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, TextWriter output, TextWriter error) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Information and < LogLevel.None;

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

            var writer = logLevel >= LogLevel.Error ? error : output;
            writer.WriteLine($"[{category}] {logLevel}: {formatter(state, exception)}");
            if (exception is not null)
            {
                writer.WriteLine(exception.ToString());
            }
        }
    }
}
