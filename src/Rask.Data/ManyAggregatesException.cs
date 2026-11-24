namespace Rask.Data;

/// <summary>
///     Thrown when a <c>Db.Transaction</c> writes a second aggregate without saying why, under
///     <see cref="AggregatePolicy.Throw" /> — the save is refused and the transaction rolls back.
/// </summary>
/// <remarks>
///     A design refusal, not a user's: the fix is in the code — one aggregate per transaction (a domain event for the
///     other), or <c>Db.Transaction(async () =&gt; …, manyAggregates: "why")</c> where they are only valid together.
/// </remarks>
public sealed class ManyAggregatesException : InvalidOperationException
{
    /// <summary>Creates the exception with the default message.</summary>
    public ManyAggregatesException()
        : this("A Db.Transaction wrote more than one aggregate without manyAggregates: \"why\".")
    {
    }

    /// <summary>Creates the exception with a supplied message.</summary>
    /// <param name="message">The message.</param>
    public ManyAggregatesException(string message)
        : this(message, innerException: null)
    {
    }

    /// <summary>Creates the exception with a supplied message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ManyAggregatesException(string message, Exception? innerException)
        : base(message, innerException) =>
        Aggregates = [];

    internal ManyAggregatesException(IReadOnlyList<string> aggregates)
        : base(MessageFor(aggregates)) =>
        Aggregates = aggregates;

    /// <summary>The aggregates the transaction wrote, each as <c>Type key</c> — <c>Supply 42</c>, <c>Tank 7</c>.</summary>
    public IReadOnlyList<string> Aggregates { get; }

    /// <summary>What the refusal, and the warning <see cref="AggregatePolicy.Warn" /> logs, says.</summary>
    internal static string MessageFor(IReadOnlyList<string> aggregates) =>
        $"This Db.Transaction writes {aggregates.Count} aggregates — {string.Join(", ", aggregates)} — and says " +
        "nowhere why. An aggregate is the transaction boundary: write one per transaction (the other through a " +
        "domain event), or state why they are only valid together: " +
        "Db.Transaction(async () => …, manyAggregates: \"why\").";
}
