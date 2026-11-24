namespace Rask.Data;

public static partial class Db
{
    /// <summary>
    ///     Records every <c>Db.Transaction</c> that commits in this flow until disposed, with the aggregates each wrote —
    ///     for a test to say what a transaction boundary is.
    /// </summary>
    /// <returns>The recording; dispose it to stop.</returns>
    /// <remarks>
    ///     <code>
    /// using var saves = Db.Record();
    /// await DeliveryNote.Write(note, 0);
    /// saves.EachWroteOneAggregate();
    ///     </code>
    ///     It counts as <see cref="RaskDataOptions.OneAggregatePerTransaction" /> does — a child as its aggregate, a table
    ///     of infrastructure not at all — whatever the policy, and also a transaction that stated <c>manyAggregates:</c>.
    ///     A recording inside another one is the one recorded to until it is disposed.
    /// </remarks>
    public static TransactionRecording Record() => new();
}
