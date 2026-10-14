using Microsoft.Extensions.Logging;

namespace Rask.Data;

/// <summary>EF Core's logger for saves and commands, asking <see cref="ViolationLogging" /> before an entry is written.</summary>
/// <param name="inner">The app's logger for the category.</param>
/// <param name="owner">What decides whether an entry is a refusal the model words as a sentence.</param>
internal sealed class ViolationLogger(ILogger inner, ViolationLogging owner) : ILogger
{
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
        if (owner.Drops(eventId, exception))
        {
            return;
        }

        if (owner.Holds(eventId))
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
