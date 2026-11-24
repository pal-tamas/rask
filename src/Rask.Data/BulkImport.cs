using System.Diagnostics.CodeAnalysis;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     <c>X.Import</c>: a large load, every row made by the aggregate's own factory and saved through its rules, a batch
///     to a transaction.
/// </summary>
/// <remarks>
///     <para>
///         A batch is built, matched to what is stored (<see cref="ImportOptions{T, TRow}.Upsert{TKey}" />), staged on one
///         context and saved in one go, in a <c>Db.Transaction</c> that says why it writes many aggregates — each row is
///         its own, batched only for speed. Its guards (<c>LockedBy</c>, the unique checks, <c>Validate()</c>) and
///         events run as for any save.
///     </para>
///     <para>
///         A batch one row of which is refused rolls back whole. Under <see cref="ImportFailure.Collect" /> it is then
///         run again row by row, each in a transaction of its own, so the refused rows are set aside with their
///         position and the rest are saved — on every database alike: a refusal a database raises (a unique index)
///         ends PostgreSQL's transaction, which is why no batch goes on past one. A factory is therefore called again
///         for a batch that is retried: it builds, it does not write.
///     </para>
/// </remarks>
internal static class BulkImport
{
    internal static async Task<ImportResult<TRow>> Run<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow>(
        IEnumerable<TRow> rows, Func<TRow, T> make, Action<ImportOptions<T, TRow>>? configure, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(make);
        cancellationToken = Ambient.Or(cancellationToken);

        var options = new ImportOptions<T, TRow>();
        configure?.Invoke(options);
        BulkEach.CheckBatchSize(options.BatchSize, nameof(configure));
        if (options.OnInvalid == ImportFailure.Collect && AmbientTransaction.Current is not null)
        {
            throw new InvalidOperationException(
                $"{typeof(T).Name}.Import(…) with OnInvalid = ImportFailure.Collect commits a batch at a time, which it cannot " +
                "inside a Db.Transaction: call it outside one, or leave OnInvalid at ImportFailure.Stop to make the import " +
                "part of the transaction.");
        }

        Tenant.DemandForWrite(typeof(T));
        var reason = $"{typeof(T).Name}.Import: each row is a {typeof(T).Name} of its own, saved {options.BatchSize} to a transaction";
        var failures = new List<RefusedRow<TRow>>();
        var (inserted, updated) = (0, 0);
        var at = 0;
        foreach (var chunk in rows.Chunk(options.BatchSize))
        {
            var batch = chunk.Select((row, i) => (Index: at + i, Row: row)).ToArray();
            at += chunk.Length;

            Saved<TRow> saved;
            try
            {
                saved = await Db.Transaction(() => Batch(batch, make, options, cancellationToken), reason, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RaskValidationException) when (options.OnInvalid == ImportFailure.Collect)
            {
                saved = await RowByRow(batch, make, options, reason, cancellationToken).ConfigureAwait(false);
            }

            inserted += saved.Inserted;
            updated += saved.Updated;
            failures.AddRange(saved.Refused);
        }

        return new ImportResult<TRow>(inserted, updated, failures);
    }

    private static async Task<Saved<TRow>> RowByRow<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow>(
        (int Index, TRow Row)[] batch, Func<TRow, T> make, ImportOptions<T, TRow> options, string reason,
        CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        var all = new Saved<TRow>(0, 0, []);
        foreach (var item in batch)
        {
            try
            {
                var one = await Db.Transaction(() => Batch([item], make, options, cancellationToken), reason, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                all = all.And(one);
            }
            catch (RaskValidationException refused)
            {
                all = all.And(new Saved<TRow>(0, 0, [new RefusedRow<TRow>(item.Index, item.Row, refused)]));
            }
        }

        return all;
    }

    // One context for the batch: each row built, matched to the stored one or to an earlier row with its key, staged,
    // and the lot saved once.
    private static async Task<Saved<TRow>> Batch<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow>(
        (int Index, TRow Row)[] batch, Func<TRow, T> make, ImportOptions<T, TRow> options, CancellationToken cancellationToken)
        where T : class, IAggregate
    {
        var refused = new List<RefusedRow<TRow>>();
        var built = new List<(int Index, TRow Row, T Aggregate)>(batch.Length);
        foreach (var (index, row) in batch)
        {
            try
            {
                built.Add((index, row, make(row)));
            }
            catch (RaskValidationException refusal) when (options.OnInvalid == ImportFailure.Collect)
            {
                refused.Add(new RefusedRow<TRow>(index, row, refusal));
            }
        }

        var context = Db.CreateContext();
        await using var contextScope = context.ConfigureAwait(false);

        var match = options.Match;
        var stored = match is null
            ? []
            : await match.Stored(context, [.. built.Select(static b => b.Aggregate)], cancellationToken).ConfigureAwait(false);
        var byKey = new Dictionary<object, T>();
        var (inserted, updated) = (0, 0);
        foreach (var (_, row, aggregate) in built)
        {
            var key = match?.KeyOf(aggregate);
            if (key is not null && byKey.TryGetValue(key, out var held))
            {
                match!.Update(held, row);
                updated++;
                continue;
            }

            var saving = aggregate;
            if (key is not null && stored.TryGetValue(key, out var id))
            {
                saving = await GeneratedModelWrites.Find<T>(id, cancellationToken).ConfigureAwait(false)
                         ?? throw GeneratedModelWrites.Missing<T>(id);
                match!.Update(saving, row);
                updated++;
            }
            else
            {
                inserted++;
            }

            if (key is not null)
            {
                byKey[key] = saving;
            }

            await GeneratedModelWrites.Save(saving, context, cancellationToken).ConfigureAwait(false);
        }

        await StaleWrite.SaveAsync(context, cancellationToken).ConfigureAwait(false);
        return new Saved<TRow>(inserted, updated, refused);
    }

    /// <summary>What a batch saved, and the rows its factory refused.</summary>
    private sealed record Saved<TRowOf>(int Inserted, int Updated, IReadOnlyList<RefusedRow<TRowOf>> Refused)
    {
        internal Saved<TRowOf> And(Saved<TRowOf> other) =>
            new(Inserted + other.Inserted, Updated + other.Updated, [.. Refused, .. other.Refused]);
    }
}
