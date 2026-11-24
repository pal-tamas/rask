namespace Rask.Data;

/// <summary>What <c>X.Import</c> wrote, and what it set aside.</summary>
/// <typeparam name="TRow">The rows imported.</typeparam>
public sealed class ImportResult<TRow>
{
    internal ImportResult(int inserted, int updated, IReadOnlyList<RefusedRow<TRow>> failures) =>
        (Inserted, Updated, Failures) = (inserted, updated, failures);

    /// <summary>How many rows were saved as new aggregates.</summary>
    public int Inserted { get; }

    /// <summary>How many rows changed an aggregate already stored (<see cref="ImportOptions{T, TRow}.Upsert{TKey}" />).</summary>
    public int Updated { get; }

    /// <summary>
    ///     The rows refused and set aside, in the order given — always empty unless <see cref="ImportOptions{T, TRow}.OnInvalid" />
    ///     is <see cref="ImportFailure.Collect" />.
    /// </summary>
    public IReadOnlyList<RefusedRow<TRow>> Failures { get; }
}
