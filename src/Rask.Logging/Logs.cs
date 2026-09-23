using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Logging;

/// <summary>
///     The app's durable log, with nothing injected — from a handler, a render, a request, a job:
/// </summary>
/// <remarks>
///     <code>
///     var page = await Logs.Search(new LogQuery { MinimumLevel = LogLevel.Warning });
///     var categories = await Logs.Categories();
///     var total = await Logs.Count();
///     await Logs.Trim().OlderThan(30.Days).KeepingNewest(100_000);
///     </code>
///     <para>
///         Each call reaches the <see cref="ILogs" /> of the work it runs in and is cancelled with that work.
///         Outside any — a hosted service, a timer started at boot — it throws; inject <see cref="ILogs" />
///         there instead.
///     </para>
/// </remarks>
public static class Logs
{
    /// <summary>One page of entries matching <paramref name="query" />, newest first.</summary>
    /// <param name="query">The filter. Every property is optional.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static Task<LogPage> Search(LogQuery query, CancellationToken cancellationToken = default) =>
        Resolve().Search(query, Ambient.Or(cancellationToken));

    /// <summary>The distinct categories currently stored, ordered, for a filter dropdown.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static Task<IReadOnlyList<string>> Categories(CancellationToken cancellationToken = default) =>
        Resolve().Categories(Ambient.Or(cancellationToken));

    /// <summary>How many entries the store currently holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static Task<long> Count(CancellationToken cancellationToken = default) =>
        Resolve().Count(Ambient.Or(cancellationToken));

    /// <summary>
    ///     Enforces retention: <c>await Logs.Trim().OlderThan(30.Days)</c>,
    ///     <c>.KeepingNewest(100_000)</c>, or both. Nothing is removed until it is awaited, and a
    ///     <c>Trim()</c> with neither step removes nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the trim.</param>
    public static Trimming Trim(CancellationToken cancellationToken = default) =>
        new(null, null, null, cancellationToken);

    /// <summary>Deletes every stored entry.</summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    public static Task Clear(CancellationToken cancellationToken = default) =>
        Resolve().Clear(Ambient.Or(cancellationToken));

    /// <summary>
    ///     Whether a log store is registered at all. For an operator surface that renders "logging is off"
    ///     rather than failing — <c>Rask.Dashboard</c>'s Logs page does exactly that. An app should not
    ///     branch on this: a call with no store throws and names the registration that fixes it.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool IsOn =>
        Faked.Value is not null || Ambient.Services?.GetService<ILogs>() is not null;

    /// <summary>What <c>Logs.Fake()</c> put in the way of the real store, for this test's flow alone.</summary>
    internal static readonly AsyncLocal<ILogs?> Faked = new();

    internal static ILogs Resolve()
    {
        if (Faked.Value is { } fake)
        {
            return fake;
        }

        var services = Ambient.Services
            ?? throw new InvalidOperationException(
                "Logs was called outside any work in progress — a handler, a render, a request or a job — so "
                + "there is no app to reach. Inject ILogs in the constructor there instead.");

        return services.GetService<ILogs>()
            ?? throw new InvalidOperationException(
                "Logs needs Rask.Logging registered: call builder.Services.AddRaskLogging<AppDbContext>().");
    }
}

/// <summary>
///     A <c>Trim</c> still being worded: <c>await Logs.Trim().OlderThan(30.Days)</c>. Nothing is removed
///     until it is awaited.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Trimming
{
    private readonly ILogs? _logs;
    private readonly TimeSpan? _olderThan;
    private readonly int? _keepNewest;
    private readonly CancellationToken _cancellationToken;

    internal Trimming(ILogs? logs, TimeSpan? olderThan, int? keepNewest, CancellationToken cancellationToken)
    {
        _logs = logs;
        _olderThan = olderThan;
        _keepNewest = keepNewest;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Drops entries older than <paramref name="age" />: <c>.OlderThan(30.Days)</c>.</summary>
    /// <param name="age">How far back to keep.</param>
    public Trimming OlderThan(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(age), age, "OlderThan takes a positive age — to remove everything, call Logs.Clear().");
        }

        return new Trimming(_logs, age, _keepNewest, _cancellationToken);
    }

    /// <summary>Keeps at most <paramref name="rows" /> entries, newest first: <c>.KeepingNewest(100_000)</c>.</summary>
    /// <param name="rows">How many to keep.</param>
    public Trimming KeepingNewest(int rows)
    {
        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rows), rows, "KeepingNewest takes at least 1 — to remove everything, call Logs.Clear().");
        }

        return new Trimming(_logs, _olderThan, rows, _cancellationToken);
    }

    /// <summary>Runs it, handing back how many entries were removed.</summary>
    public TaskAwaiter<int> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<int> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}" />.</summary>
    public Task<int> AsTask() =>
        (_logs ?? Logs.Resolve()).Trim(_olderThan, _keepNewest, Ambient.Or(_cancellationToken));
}

/// <summary>The trim steps on an injected <see cref="ILogs" />, worded as on <see cref="Logs" />.</summary>
public static class LogsExtensions
{
    extension(ILogs logs)
    {
        /// <inheritdoc cref="Logs.Trim(CancellationToken)" />
        public Trimming Trim(CancellationToken cancellationToken = default) =>
            new(logs ?? throw new ArgumentNullException(nameof(logs)), null, null, cancellationToken);
    }
}
