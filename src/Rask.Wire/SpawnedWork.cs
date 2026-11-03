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

    /// <summary>
    ///     Starts <paramref name="start" /> in <paramref name="flow" /> rather than its starter's, and outside the unit
    ///     of work in progress there (<see cref="Start{TState}" />). What it runs after its first wait keeps that flow.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For work a handler's own code sets off while the handler is inside a scope of its own — a repaint between
    ///         its awaits, made from the continuation that came back. Inherited, the repaint saw every
    ///         <see cref="AsyncLocal{T}" /> the handler had opened: inside <c>Tenant.Across()</c> the page rendered
    ///         with no tenant, and showed its "no tenant" branch for one frame on every click. Started in the flow the
    ///         handler was dispatched in, it sees what the render after the handler sees: the session's tenant, user and
    ///         services. One mechanism for every <see cref="AsyncLocal{T}" />, Rask's or the app's.
    ///     </para>
    ///     <para>
    ///         A null <paramref name="flow" />, or one the starter is already in, starts as <see cref="Start{TState}" />
    ///         does: switching costs nothing when nothing would change, and the render path allocates nothing either way.
    ///     </para>
    /// </remarks>
    /// <param name="flow">The flow to start in — captured where the work's owner was dispatched — or null for the starter's.</param>
    /// <param name="start">What to start.</param>
    /// <param name="state">What it is started with.</param>
    /// <returns>The started work.</returns>
    internal static Task StartIn<TState>(ExecutionContext? flow, Func<TState, Task> start, TState state)
    {
        // Null when the starter suppressed its flow: there is then nothing to come back to, so nothing is switched.
        var here = ExecutionContext.Capture();
        if (flow is null || here is null || ReferenceEquals(flow, here))
        {
            return Start(start, state);
        }

        ExecutionContext.Restore(flow);
        try
        {
            return Start(start, state);
        }
        finally
        {
            ExecutionContext.Restore(here);
        }
    }
}
