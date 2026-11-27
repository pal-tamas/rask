using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Cqrs;

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
///         The checks and the save share ONE transaction (<see cref="SaveTransaction" />): when the caller has not
///         opened one, this does, and closes it when the save ends.
///     </para>
/// </remarks>
internal sealed class DeclaredRuleCheckInterceptor : SaveChangesInterceptor
{
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

    private static async Task CheckAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var rows = UniqueRulesOfSave.Touched(context);
        var ranges = NoOverlapRulesOfSave.Touched(context);
        if (rows.Count == 0 && ranges.Count == 0)
        {
            return;
        }

        await OpenAsync(context, cancellationToken).ConfigureAwait(false);

        try
        {
            // The tenant is stamped by now, so every value is the row's own.
            var failures = await UniqueRulesOfSave.FailuresAsync(
                context, rows, static (row, name) => row.Property(name).CurrentValue, static _ => "", cancellationToken).ConfigureAwait(false);
            failures.AddRange(await NoOverlapRulesOfSave.FailuresAsync(
                context, ranges, static (row, name) => row.Property(name).CurrentValue, static _ => "", cancellationToken).ConfigureAwait(false));
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

    private static Task OpenAsync(DbContext context, CancellationToken cancellationToken) =>
        SaveTransaction.Open(context, cancellationToken);

    private static void Close(DbContext? context, bool commit) => SaveTransaction.Close(context, commit);

    private static Task CloseAsync(DbContext? context, bool commit, CancellationToken cancellationToken) =>
        SaveTransaction.Close(context, commit, cancellationToken);
}
