using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     The logger factory EF Core logs through, for a context whose refusals <see cref="ViolationMessageInterceptor" />
///     answers with the model's sentence: the error EF Core logs for such a save is not written.
/// </summary>
/// <remarks>
///     <para>
///         EF Core logs a failed save (<c>Microsoft.EntityFrameworkCore.Update[10000]</c>) and the command that
///         failed (<c>Microsoft.EntityFrameworkCore.Database.Command[20102]</c>) at Error BEFORE it tells any
///         interceptor, so no interceptor can take either back. The logger decides instead: the failed command
///         is held until the save's own failure is logged, and the two are dropped only when the model has a
///         sentence for that failure, and only for that very exception instance — the save's, and the command's
///         inside it. Everything else is written, in its order, through the app's own logger, scopes and all.
///     </para>
///     <para>
///         Held only while <see cref="ViolationMessageInterceptor" /> is watching a save of this context: without
///         it nothing is translated, so nothing is quiet.
///     </para>
/// </remarks>
/// <param name="app">The factory EF Core would have logged through.</param>
internal sealed partial class ViolationLogging(ILoggerFactory app) : ILoggerFactory
{
    private readonly List<(Exception? Failure, Action Write)> _held = [];
    private DbContext? _saving;
    private (Exception Failure, RaskValidationException Violation)? _translated;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        var logger = app.CreateLogger(categoryName);
        return categoryName switch
        {
            UpdateCategory => new ViolationLogger(logger, this, ViolationLogger.Watches.FailedSave),
            CommandCategory => new ViolationLogger(logger, this, ViolationLogger.Watches.FailedCommand),
            _ => logger,
        };
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => app.AddProvider(provider);

    /// <summary>The app's factory is not this one's to dispose.</summary>
    public void Dispose()
    {
    }

    /// <summary>The one <paramref name="context" /> logs through, when it has one.</summary>
    internal static ViolationLogging? Of(DbContext? context) =>
        context?.GetInfrastructure().GetService(typeof(ViolationLogging)) as ViolationLogging;

    internal void SaveStarting(DbContext context)
    {
        SaveEnded(translated: null);
        _saving = context;
    }

    /// <summary>
    ///     Ends the save: the held entries about <paramref name="translated" /> — the very exception the model's
    ///     sentence replaced — are dropped, every other one is written.
    /// </summary>
    internal void SaveEnded(Exception? translated)
    {
        Release(except: translated?.InnerException);
        _saving = null;
        _translated = null;
    }

    /// <summary>
    ///     The sentence this save's <paramref name="failure" /> was already found to stand for, when it is the
    ///     same exception instance the failed save was logged with.
    /// </summary>
    internal RaskValidationException? TranslationOf(Exception? failure) =>
        _translated is var (logged, violation) && ReferenceEquals(logged, failure) ? violation : null;

    /// <summary>Whether a failed save's entry is about a failure the model words as a sentence.</summary>
    internal bool Drops(Exception? failure)
    {
        if (_saving is not { } context || failure is null ||
            ViolationMessageInterceptor.Translation(context, failure) is not { } violation)
        {
            SaveEnded(translated: null);
            return false;
        }

        // Kept for the interceptor, which throws exactly this translation for exactly this exception.
        Release(except: failure.InnerException);
        _translated = (failure, violation);

        var rask = app.CreateLogger(typeof(ViolationLogging).Namespace!);
        if (rask.IsEnabled(LogLevel.Debug))
        {
            Refused(rask, context.GetType().Name, failure);
        }

        return true;
    }

    /// <summary>Whether a failed command is part of a save, held until the save's failure says what it was.</summary>
    internal bool Holds => _saving is not null;

    internal void Hold(Action write) => _held.Add((null, write));

    /// <summary>
    ///     Names the exception of the failed command just held: EF Core logs it without one, and hands the instance
    ///     to the command interceptors right after.
    /// </summary>
    internal void CommandFailed(Exception failure)
    {
        if (_held is [.., (null, var write)])
        {
            _held[^1] = (failure, write);
        }
    }

    // Writes what was held, but for the failed command whose exception is the one the sentence replaced.
    private void Release(Exception? except)
    {
        foreach (var (failure, write) in _held)
        {
            if (except is null || !ReferenceEquals(failure, except))
            {
                write();
            }
        }

        _held.Clear();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A save of {Context} was refused, and answered with the model's sentence.")]
    private static partial void Refused(ILogger logger, string context, Exception? exception);

    private const string UpdateCategory = "Microsoft.EntityFrameworkCore.Update";
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";
}
