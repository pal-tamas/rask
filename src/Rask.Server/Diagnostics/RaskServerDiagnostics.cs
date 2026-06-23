using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;

namespace Rask.Server.Diagnostics;

/// <summary>
///     Bridges the framework's dependency-free <see cref="RaskDiagnostics" /> seam to the host's
///     <c>ILogger</c> pipeline. Installed once by <c>UseRask&lt;TApp&gt;()</c>: from then on every
///     framework diagnostic — a faulting lifecycle hook, a duplicate sibling key, a malformed WS
///     frame, a handler that threw — is logged through an <c>ILogger</c> named for the event's
///     <see cref="RaskDiagnosticEvent.Category" /> (e.g. <c>Rask.Lifecycle</c>, <c>Rask.Diff</c>,
///     <c>Rask.Live</c>), at the mapped <see cref="LogLevel" />, with the original exception attached.
///     Without this bridge the seam keeps its default behaviour (a plain <c>Console.Error</c> writer),
///     so logging is opt-in via the host but on by default for any Rask server app.
/// </summary>
internal static class RaskServerDiagnostics
{
    private static readonly ConcurrentDictionary<string, ILogger> Loggers = new(StringComparer.Ordinal);
    private static ILoggerFactory? _factory;

    /// <summary>
    ///     Routes <see cref="RaskDiagnostics.Sink" /> into <paramref name="factory" />. A no-op when no
    ///     logger factory is available (the seam then keeps its stderr default). Idempotent: re-installing
    ///     simply repoints at the latest factory.
    /// </summary>
    public static void Install(ILoggerFactory? factory)
    {
        if (factory is null)
        {
            return;
        }

        _factory = factory;
        Loggers.Clear();
        RaskDiagnostics.Sink = Forward;
    }

    private static void Forward(RaskDiagnosticEvent e)
    {
        var factory = _factory;
        if (factory is null)
        {
            return;
        }

        var logger = Loggers.GetOrAdd(e.Category, static (category, f) => f.CreateLogger(category), factory);

        // The message is already a fully-formed human string; log it as a single structured field so the
        // exception travels as a first-class argument rather than being concatenated into the text.
        logger.Log(Map(e.Level), e.Exception, "{RaskMessage}", e.Message);
    }

    private static LogLevel Map(RaskLogLevel level) => level switch
    {
        RaskLogLevel.Error => LogLevel.Error,
        RaskLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Information
    };
}
