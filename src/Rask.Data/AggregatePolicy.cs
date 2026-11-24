namespace Rask.Data;

/// <summary>
///     What a <c>Db.Transaction</c> that writes a second aggregate without saying why meets —
///     <see cref="RaskDataOptions.OneAggregatePerTransaction" />.
/// </summary>
public enum AggregatePolicy
{
    /// <summary>Nothing: a transaction writes what it writes.</summary>
    Off,

    /// <summary>A warning in the log, naming the aggregates, once per transaction; the transaction goes on.</summary>
    Warn,

    /// <summary>The save is refused with a <see cref="ManyAggregatesException" />, and the transaction rolls back.</summary>
    Throw,
}
