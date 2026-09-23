using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Query;

/// <summary>
///     A <c>Send</c> still being worded: <c>await ship.Send(cmd).Optimistically(…)</c>. Nothing is
///     dispatched until it is awaited.
/// </summary>
/// <remarks>
///     Named <c>Dispatching</c> rather than <c>Sending</c> because <c>Rask.Mail</c> has one of those, and
///     a file that reaches for both should never have to say which it meant.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Dispatching
{
    private readonly Func<OptimisticEdit[], CancellationToken, Task> _run;
    private readonly OptimisticEdit[] _optimistic;
    private readonly CancellationToken _cancellationToken;

    internal Dispatching(
        Func<OptimisticEdit[], CancellationToken, Task> run,
        OptimisticEdit[] optimistic,
        CancellationToken cancellationToken)
    {
        _run = run;
        _optimistic = optimistic;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Shows <paramref name="edits" /> at once, replaces them with the refetch on success, and puts the
    ///     old data back if the server refuses: <c>.Optimistically(_list.Optimistic(o =&gt; …))</c>.
    /// </summary>
    /// <param name="edits">What to show before the server answers, from a query's <c>Optimistic</c>.</param>
    public Dispatching Optimistically(params OptimisticEdit[] edits) =>
        new(_run, edits ?? throw new ArgumentNullException(nameof(edits)), _cancellationToken);

    /// <summary>Runs it.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task" />.</summary>
    public Task AsTask() => _run(_optimistic, _cancellationToken);
}

/// <summary>
///     A <c>Send</c> that hands back what the command returned, still being worded. Nothing is dispatched
///     until it is awaited.
/// </summary>
/// <typeparam name="TResult">What the command returns.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Dispatching<TResult>
{
    private readonly Func<OptimisticEdit[], CancellationToken, Task<TResult?>> _run;
    private readonly OptimisticEdit[] _optimistic;
    private readonly CancellationToken _cancellationToken;

    internal Dispatching(
        Func<OptimisticEdit[], CancellationToken, Task<TResult?>> run,
        OptimisticEdit[] optimistic,
        CancellationToken cancellationToken)
    {
        _run = run;
        _optimistic = optimistic;
        _cancellationToken = cancellationToken;
    }

    /// <inheritdoc cref="Dispatching.Optimistically" />
    /// <param name="edits">What to show before the server answers, from a query's <c>Optimistic</c>.</param>
    public Dispatching<TResult> Optimistically(params OptimisticEdit[] edits) =>
        new(_run, edits ?? throw new ArgumentNullException(nameof(edits)), _cancellationToken);

    /// <summary>Runs it.</summary>
    public TaskAwaiter<TResult?> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<TResult?> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}" />.</summary>
    public Task<TResult?> AsTask() => _run(_optimistic, _cancellationToken);
}
