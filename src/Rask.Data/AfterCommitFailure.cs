namespace Rask.Data;

/// <summary>
///     What a step that runs after a <c>Db.Transaction</c> commits — an in-memory event handler, a data-changed notice —
///     meets when it throws: <see cref="RaskDataOptions.AfterCommitFailures" />.
/// </summary>
public enum AfterCommitFailure
{
    /// <summary>
    ///     An error in the log with the step's exception; every other step still runs, and the transaction's caller sees
    ///     it succeed — it did: the work is committed.
    /// </summary>
    Log,

    /// <summary>
    ///     Every step still runs, and then the caller gets the failure: the exception itself when one step threw, an
    ///     <see cref="AggregateException" /> when several did. The work is committed all the same.
    /// </summary>
    Throw,
}
