using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;

namespace Rask.Wasm.Diagnostics;

/// <summary>
///     Bridges the framework's dependency-free <see cref="RaskDiagnostics" /> seam to the browser app's
///     <c>ILogger</c> pipeline — the WASM sibling of the server's bridge, installed once by
///     <c>WasmHostBuilder.RunAsync</c>.
/// </summary>
/// <remarks>
///     <para>
///         Without it, a WASM app was the one host where framework faults never reached the app's own
///         logging at all: swallow-and-log is the framework's primary failure mode for navigate faults,
///         JS dispatch faults and malformed frames, and all of them went to the seam's stderr default
///         while the app's configured providers saw nothing. The host already calls
///         <c>Services.AddLogging()</c>, so the factory was there the whole time; nothing consumed it.
///     </para>
///     <para>
///         Deliberately a sibling rather than a shared type. The two hosts live in packages that share no
///         assembly carrying <c>Microsoft.Extensions.Logging</c> — <c>Rask.Core</c> cannot take that
///         dependency, which is the entire reason this seam exists — and giving <c>Rask.Server</c> a
///         reference to a browser-side package to dedupe sixty lines would trade a real packaging
///         constraint for a cosmetic one. <c>RaskDiagnosticsBridgeParityTests</c> pins the two
///         implementations to the same level mapping so the copies cannot drift.
///     </para>
/// </remarks>
internal static class RaskWasmDiagnostics
{
    private static ILoggerFactory? _factory;

    /// <summary>
    ///     Routes <see cref="RaskDiagnostics.Sink" /> into <paramref name="factory" />. A no-op when no
    ///     logger factory is available, leaving the seam's stderr default in place.
    /// </summary>
    public static void Install(ILoggerFactory? factory)
    {
        if (factory is null)
        {
            return;
        }

        // No lock, unlike the server bridge: a browser app is single-threaded and there is exactly one
        // host per process, so the multi-host race that one guards against cannot arise here.
        _factory = factory;
        RaskDiagnostics.Sink = Forward;
    }

    private static void Forward(RaskDiagnosticEvent e)
    {
        if (_factory is { } factory)
        {
            Emit(factory, e);
        }
    }

    /// <summary>
    ///     Log one event. A diagnostic must never become a fault — these call sites are inside the
    ///     framework's own catch blocks, so an exception escaping here would turn a swallowed warning
    ///     into a torn-down session. Swallow and fall back to stderr.
    /// </summary>
    internal static void Emit(ILoggerFactory factory, RaskDiagnosticEvent e)
    {
        try
        {
            factory.CreateLogger(e.Category).Log(Map(e.Level), e.Exception, "{RaskMessage}", e.Message);
        }
        catch
        {
            try
            {
                Console.Error.WriteLine(RaskDiagnostics.FormatDefault(e));
            }
            catch
            {
                // Nothing left to do — never rethrow out of a diagnostic.
            }
        }
    }

    internal static LogLevel Map(RaskLogLevel level) => level switch
    {
        RaskLogLevel.Error => LogLevel.Error,
        RaskLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Information
    };
}
