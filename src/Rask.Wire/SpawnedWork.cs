namespace Rask;

/// <summary>
///     Starts what the framework runs from inside a unit of work, beside it or after it, without that work: a repaint,
///     a render a <c>StateHasChanged</c> asks for, a query's fetch, a subscription's stream.
/// </summary>
/// <remarks>
///     <para>
///         Such work inherits its starter's <see cref="ExecutionContext" />, and with it every
///         <see cref="AsyncLocal{T}" /> the starter holds. Rask.Data's <c>Db.Transaction</c> is one: a repaint made
///         while a handler's transaction was open ran the page's reads on that transaction's connection, at its
///         isolation level, so a read the app made only outside its transactions took serializable range locks and
///         deadlocked. The work the block runs itself stays in the transaction; only what Rask starts leaves it.
///     </para>
///     <para>
///         Here because every side reaches it and none reaches the others: Rask.Data sets the hook, Rask.Core and
///         Rask.Query start their work through it. Unset (no transaction ever opened) it is one null check, and outside
///         a transaction the hook hands back <see langword="null" />, so the render path allocates nothing.
///     </para>
/// </remarks>
internal static class SpawnedWork
{
    /// <summary>
    ///     Clears the flow's unit of work until the returned scope is disposed; <see langword="null" /> when there is
    ///     none in progress. Set by Rask.Data.
    /// </summary>
    internal static Func<IDisposable?>? Leave { get; set; }

    /// <summary>
    ///     Starts <paramref name="start" /> outside the unit of work in progress. What it runs after its first wait
    ///     keeps the flow it was started in, so it stays outside too.
    /// </summary>
    /// <returns>The started work.</returns>
    internal static Task Start<TState>(Func<TState, Task> start, TState state)
    {
        var left = Leave?.Invoke();
        try
        {
            return start(state);
        }
        finally
        {
            left?.Dispose();
        }
    }
}
