using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

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
///         is held until the save's own failure is logged, and both are dropped when the model has a sentence for
///         that failure — or written, in their order, when it has not.
///     </para>
///     <para>
///         Held only while <see cref="ViolationMessageInterceptor" /> is watching a save of this context: without
///         it nothing is translated, so nothing is quiet.
///     </para>
/// </remarks>
/// <param name="app">The factory EF Core would have logged through.</param>
internal sealed partial class ViolationLogging(ILoggerFactory app) : ILoggerFactory
{
    private readonly List<Action> _held = [];
    private DbContext? _saving;

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        var logger = app.CreateLogger(categoryName);
        return categoryName is UpdateCategory or CommandCategory ? new ViolationLogger(logger, this) : logger;
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
        Release();
        _saving = context;
    }

    internal void SaveEnded(bool translated)
    {
        if (translated)
        {
            _held.Clear();
        }

        Release();
        _saving = null;
    }

    /// <summary>Whether the entry is EF Core's error for a save the model words as a sentence.</summary>
    internal bool Drops(EventId id, Exception? exception)
    {
        if (id.Id != CoreEventId.SaveChangesFailed.Id || _saving is not { } context)
        {
            return false;
        }

        var translated = ViolationMessageInterceptor.Translation(context, exception) is not null;
        SaveEnded(translated);

        var rask = app.CreateLogger(typeof(ViolationLogging).Namespace!);
        if (translated && rask.IsEnabled(LogLevel.Debug))
        {
            Refused(rask, context.GetType().Name, exception);
        }

        return translated;
    }

    /// <summary>Whether the entry is a failed command of a save, held until the save's failure says what it was.</summary>
    internal bool Holds(EventId id) => id.Id == RelationalEventId.CommandError.Id && _saving is not null;

    internal void Hold(Action write) => _held.Add(write);

    private void Release()
    {
        foreach (var write in _held)
        {
            write();
        }

        _held.Clear();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "A save of {Context} was refused, and answered with the model's sentence.")]
    private static partial void Refused(ILogger logger, string context, Exception? exception);

    private const string UpdateCategory = "Microsoft.EntityFrameworkCore.Update";
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";
}
