namespace Rask.Data;

/// <summary>Settings for Rask.Data's writes, given to <c>AddRaskData(o =&gt; …)</c>.</summary>
public sealed class RaskDataOptions
{
    /// <summary>
    ///     What a <c>Db.Transaction</c> meets when it writes a second aggregate without
    ///     <c>manyAggregates: "why"</c>. An aggregate is the transaction boundary: two of them committed together is a
    ///     decision, and the reason is written where it is made.
    /// </summary>
    /// <value>
    ///     <see cref="AggregatePolicy.Warn" /> by default; a <see cref="TestDatabase" /> runs at
    ///     <see cref="AggregatePolicy.Throw" />, so a test finds the transaction that owes a reason.
    /// </value>
    /// <remarks>
    ///     Counted per transaction — a nested <c>Db.Transaction</c> joins the outermost and is counted with it — from what
    ///     its saves write, a child counted as its aggregate. Rask's own tables (the outbox, jobs, mail, sessions, stored
    ///     files) are not aggregates of the app's and never count, nor is a table the app marks <c>b.Infrastructure()</c>.
    ///     A bulk write (<c>ExecuteUpdate</c>, <c>BulkInsert</c>) is not a save and is not counted.
    /// </remarks>
    public AggregatePolicy OneAggregatePerTransaction { get; set; } = AggregatePolicy.Warn;

    /// <summary>
    ///     What a step that runs after a <c>Db.Transaction</c> commits meets when it throws — an in-memory
    ///     <c>IEventHandler</c>, a data-changed notice, the outbox's wake-up.
    /// </summary>
    /// <value>
    ///     <see cref="AfterCommitFailure.Log" /> by default: the work is committed, so its caller is not told it failed;
    ///     the error is logged and the steps after it still run.
    /// </value>
    /// <remarks>
    ///     Every step runs either way. <see cref="AfterCommitFailure.Throw" /> hands the failure to the caller after the
    ///     last one — the exception itself, or an <see cref="AggregateException" /> of several — for an app that would
    ///     rather hear it. A durable handler is not such a step: it runs from the outbox, and is retried there.
    /// </remarks>
    public AfterCommitFailure AfterCommitFailures { get; set; } = AfterCommitFailure.Log;
}
