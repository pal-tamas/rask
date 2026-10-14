using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Rask.Data;

/// <summary>EF Core's logger for saves and commands, asking <see cref="ViolationLogging" /> before an entry is written.</summary>
/// <param name="inner">The app's logger for the category.</param>
/// <param name="owner">What decides whether an entry is a refusal the model words as a sentence.</param>
/// <param name="watches">The one EF Core event of this category that may be about such a refusal.</param>
/// <remarks>Everything else — other events, scopes, <see cref="IsEnabled" /> — is the app's logger's, untouched.</remarks>
internal sealed class ViolationLogger(ILogger inner, ViolationLogging owner, ViolationLogger.Watches watches) : ILogger
{
    /// <summary>The EF Core event a logger watches.</summary>
    internal enum Watches
    {
        /// <summary><c>CoreEventId.SaveChangesFailed</c> (10000), on <c>Microsoft.EntityFrameworkCore.Update</c>.</summary>
        FailedSave,

        /// <summary><c>RelationalEventId.CommandError</c> (20102), on <c>Microsoft.EntityFrameworkCore.Database.Command</c>.</summary>
        FailedCommand,
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        inner.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (watches == Watches.FailedSave && eventId.Id == CoreEventId.SaveChangesFailed.Id && owner.Drops(exception))
        {
            return;
        }

        if (watches == Watches.FailedCommand && eventId.Id == RelationalEventId.CommandError.Id && owner.Holds)
        {
            owner.Hold(Later(inner, logLevel, eventId, state, exception, formatter));
            return;
        }

        inner.Log(logLevel, eventId, state, exception, formatter);
    }

    // Its own method, so the closure is built only for an entry that is held, not for every entry logged.
    private static Action Later<TState>(
        ILogger logger, LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        () => logger.Log(logLevel, eventId, state, exception, formatter);
}
