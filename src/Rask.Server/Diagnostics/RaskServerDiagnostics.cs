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
///     <para>
///         <see cref="RaskDiagnostics.Sink" /> is a process-global seam, so in a multi-host process
///         (e.g. a test run that spins up several <c>WebApplication</c>s) the most recently installed
///         factory wins for all hosts. That is benign — diagnostics are still logged — and
///         <see cref="Emit" /> never lets a logging fault (including a factory disposed by an earlier
///         host's teardown) escape back into the framework's own catch blocks.
///     </para>
/// </summary>
internal static class RaskServerDiagnostics
{
    private static readonly object Gate = new();
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

        lock (Gate)
        {
            // Publish the factory before the sink so a concurrent Forward always sees a non-null factory.
            _factory = factory;
            RaskDiagnostics.Sink = Forward;
        }
    }

    private static void Forward(RaskDiagnosticEvent e)
    {
        // Snapshot the settable factory so a concurrent Install/teardown can't null it out mid-call.
        var factory = _factory;
        if (factory is not null)
        {
            Emit(factory, e);
        }
    }

    /// <summary>
    ///     Log one event through <paramref name="factory" />. A diagnostic must never become a fault: a
    ///     logging pipeline that throws — a factory disposed by an earlier host's teardown in a
    ///     multi-host/test process, or a misbehaving <c>ILoggerProvider</c> — would otherwise escape
    ///     <see cref="RaskDiagnostics.Report" /> into the framework catch blocks that previously only
    ///     wrote to <c>stderr</c>, turning a swallowed warning into a torn-down session. Swallow and fall
    ///     back to stderr. Exposed internally so the level/category mapping can be unit-tested without
    ///     mutating the process-global sink. <c>ILoggerFactory.CreateLogger</c> caches loggers internally,
    ///     so no per-category cache is kept here.
    /// </summary>
    internal static void Emit(ILoggerFactory factory, RaskDiagnosticEvent e)
    {
        try
        {
            // The message is already a fully-formed human string; log it as a single structured field so
            // the exception travels as a first-class argument rather than being concatenated into the text.
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

    private static LogLevel Map(RaskLogLevel level) => level switch
    {
        RaskLogLevel.Error => LogLevel.Error,
        RaskLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Information
    };
}
