using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Before a save over a schema Rask does not own: runs every declared unique rule the save touches
///     (<see cref="DeclaredUniqueRules" />) and refuses the save with the rule's own message.
/// </summary>
/// <remarks>
///     <para>
///         Registered only by a host that adopts an existing database, after the auditing interceptor — the
///         tenant has to be stamped before a rule that names it can be asked.
///     </para>
///     <para>
///         The checks and the save share ONE transaction: when the caller has not opened one, this does, and
///         closes it when the save ends. A context whose execution strategy retries is the exception — EF Core
///         refuses a transaction it did not start there — so on such a context the checks run just before the
///         save, outside it.
///     </para>
/// </remarks>
internal sealed class DeclaredRuleCheckInterceptor : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, IDbContextTransaction> _opened = [];

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // The same work as the async path. A synchronous save has no other way to ask the database first.
        CheckAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await CheckAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Close(eventData.Context, commit: true);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await CloseAsync(eventData.Context, commit: true, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Close(eventData.Context, commit: false);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await CloseAsync(eventData.Context, commit: false, CancellationToken.None).ConfigureAwait(false);
        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Close(eventData.Context, commit: false);
        base.SaveChangesCanceled(eventData);
    }

    /// <inheritdoc />
    public override async Task SaveChangesCanceledAsync(
        DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await CloseAsync(eventData.Context, commit: false, CancellationToken.None).ConfigureAwait(false);
        await base.SaveChangesCanceledAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private async Task CheckAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || Touched(context) is not { Count: > 0 } rows)
        {
            return;
        }

        await OpenAsync(context, cancellationToken).ConfigureAwait(false);

        try
        {
            var failures = await FailuresAsync(context, rows, cancellationToken).ConfigureAwait(false);
            if (failures.Count > 0)
            {
                throw new RaskValidationException(failures);
            }
        }
        catch
        {
            // The save never starts, so nothing later is told to close what was opened here.
            await CloseAsync(context, commit: false, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    // The rows this save writes that a declared rule is about: new ones, and changed ones whose change is to a
    // column some rule names.
    private static List<EntityEntry> Touched(DbContext context) =>
        [.. context.ChangeTracker.Entries().Where(static entry =>
            entry.State is EntityState.Added or EntityState.Modified &&
            DeclaredUniqueRules.Rules(entry.Metadata).Any(rule =>
                entry.State == EntityState.Added ||
                rule.Properties.Any(p => entry.Property(p.Name).IsModified)))];

    private static async Task<List<FieldFailure>> FailuresAsync(
        DbContext context, List<EntityEntry> rows, CancellationToken cancellationToken)
    {
        var failures = new List<FieldFailure>();
        var inThisSave = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            // Two rows of one save can break a rule between them, and neither is in the database to be found.
            foreach (var rule in DeclaredUniqueRules.Rules(row.Metadata))
            {
                var values = rule.Properties.Select(p => row.Property(p.Name).CurrentValue).ToList();
                if (!values.Contains(null) &&
                    !inThisSave.Add(rule.GetDatabaseName() + "\u001f" + string.Join('\u001f', values)))
                {
                    failures.Add(UniqueViolation.FailureOf(rule));
                }
            }

            failures.AddRange(await DeclaredUniqueRules.CheckAsync(
                context,
                row.Metadata,
                name => row.Property(name).CurrentValue,
                row.State == EntityState.Modified ? KeyOf(row) : null,
                cancellationToken).ConfigureAwait(false));
        }

        return failures;
    }

    private static List<object?>? KeyOf(EntityEntry row) =>
        row.Metadata.FindPrimaryKey()?.Properties.Select(p => row.Property(p.Name).CurrentValue).ToList();

    private async Task OpenAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null ||
            context.Database.CreateExecutionStrategy().RetriesOnFailure)
        {
            return;
        }

        _opened.AddOrUpdate(
            context, await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
    }

    private void Close(DbContext? context, bool commit) =>
        CloseAsync(context, commit, CancellationToken.None).GetAwaiter().GetResult();

    private async Task CloseAsync(DbContext? context, bool commit, CancellationToken cancellationToken)
    {
        if (context is null || !_opened.TryGetValue(context, out var transaction))
        {
            return;
        }

        _opened.Remove(context);

        await using (transaction.ConfigureAwait(false))
        {
            if (commit)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
