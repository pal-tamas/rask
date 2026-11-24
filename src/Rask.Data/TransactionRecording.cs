namespace Rask.Data;

/// <summary>
///     The transactions committed while it was open — what <see cref="Db.Record" /> hands a test.
/// </summary>
public sealed class TransactionRecording : IDisposable
{
    private static readonly AsyncLocal<TransactionRecording?> Open = new();

    private readonly TransactionRecording? _previous;
    private readonly List<RecordedTransaction> _transactions = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    internal TransactionRecording()
    {
        _previous = Open.Value;
        Open.Value = this;
    }

    /// <summary>Every transaction committed so far, in the order they committed.</summary>
    public IReadOnlyList<RecordedTransaction> Transactions
    {
        get
        {
            lock (_gate)
            {
                return [.. _transactions];
            }
        }
    }

    /// <summary>The recording open in this flow, or <see langword="null" />.</summary>
    internal static TransactionRecording? Current => Open.Value is { _disposed: false } open ? open : null;

    /// <summary>
    ///     Asserts that every recorded transaction wrote one aggregate at most — <c>manyAggregates:</c> or not: a test
    ///     that asks this says the work is one aggregate's.
    /// </summary>
    /// <exception cref="ManyAggregatesException">A transaction wrote more than one, named with its aggregates.</exception>
    public void EachWroteOneAggregate()
    {
        if (Transactions.FirstOrDefault(static t => t.Aggregates.Count > 1) is { } many)
        {
            throw new ManyAggregatesException(
                ManyAggregatesException.MessageFor(many.Aggregates) +
                (many.ManyAggregates is { } reason ? $" It states manyAggregates: \"{reason}\"." : ""),
                innerException: null);
        }
    }

    /// <summary>Stops recording; the recording open before this one, if any, records again.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(Open.Value, this))
        {
            Open.Value = _previous;
        }
    }

    /// <summary>Records one committed transaction.</summary>
    internal void Add(RecordedTransaction transaction)
    {
        lock (_gate)
        {
            _transactions.Add(transaction);
        }
    }
}
