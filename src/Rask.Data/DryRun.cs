using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     An operation tried, not done: run on a copy of the aggregate as stored, its rules asked as a save would ask them,
///     and nothing written, locked or announced — what a form asks while it is filled in (F-47), so the rules it shows
///     are the domain's own.
/// </summary>
/// <remarks>
///     <para>
///         The copy is loaded afresh, so the aggregate a caller holds is never touched. The method runs on it — a
///         <c>Refuse</c> stops it as it would stop the operation — and the copy is then staged on a context that is
///         never saved, where the save's domain step (<see cref="DomainValidation" />) asks the attributes of what
///         changed, <c>Validate()</c> and the rest. The context is let go unsaved, so no row is written, no version
///         bumped and no domain event published.
///     </para>
///     <para>
///         What only the store would refuse is asked as queries, never as a write: each declared unique rule
///         (<c>IsUnique("…")</c>) a changed or new row touches, one query per rule; each reference the save sets to another
///         tenant-scoped table (<see cref="ReferencedTenantCheck" />), one query per table; and, for a delete, each
///         relationship with a sentence that a row still holds onto it (<see cref="ReferencedDeleteCheck" />). A column the
///         operation leaves as it was asks nothing.
///     </para>
///     <para>
///         Reads made by the rules run as they would — <c>Validate()</c> may ask the read face — but a
///         <c>Db.Lock</c> taken inside is not taken: a check that waited on another writer's lock would hold the form up
///         for an answer the save asks again anyway.
///     </para>
/// </remarks>
internal static class DryRun
{
    private static readonly AsyncLocal<bool> Running = new();

    /// <summary>Whether the work in flight is a dry run, which takes no lock.</summary>
    internal static bool IsRunning => Running.Value;

    /// <summary>What a <c>Db.Lock</c> inside a dry run hands back: held by nobody, let go by nothing.</summary>
    internal static IAsyncDisposable NoLock { get; } = new Unheld();

    /// <summary>What the save of what <paramref name="change" /> leaves would be refused for; empty when nothing.</summary>
    internal static Task<IReadOnlyList<FieldFailure>> Of<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        Func<Task<TAggregate>> change, CancellationToken cancellationToken)
        where TAggregate : class, IAggregate =>
        Of(change, static (context, aggregate, token) => AggregateSave.StageAsync(context, aggregate, token), SaveRefusalsAsync, cancellationToken);

    /// <summary>
    ///     What the delete of what <paramref name="find" /> loads would be refused for — its <c>ValidateDelete()</c>, and a
    ///     row that still points at it (<c>WhenReferenced("…")</c>).
    /// </summary>
    internal static Task<IReadOnlyList<FieldFailure>> OfDelete<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        Func<Task<TAggregate>> find, CancellationToken cancellationToken)
        where TAggregate : class, IAggregate =>
        Of(find, static (context, aggregate, token) => AggregateSave.StageDeleteAsync(context, aggregate, token),
            ReferencedDeleteCheck.FailuresAsync, cancellationToken);

    private static async Task<IReadOnlyList<FieldFailure>> Of<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        Func<Task<TAggregate>> change,
        Func<DbContext, TAggregate, CancellationToken, Task> stage,
        Func<DbContext, CancellationToken, Task<List<FieldFailure>>> store,
        CancellationToken cancellationToken)
        where TAggregate : class, IAggregate
    {
        var outer = Running.Value;
        Running.Value = true;
        try
        {
            var aggregate = await change().ConfigureAwait(false);

            var context = Db.CreateContext();
            await using var contextScope = context.ConfigureAwait(false);
            await stage(context, aggregate, cancellationToken).ConfigureAwait(false);

            var failures = new List<FieldFailure>();
            try
            {
                await DomainValidation.CheckAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (RaskValidationException refused)
            {
                failures.AddRange(refused.Failures);
            }

            // What only the store would refuse — a name already taken, a reference to no row — asked as queries of
            // what the save changes, every one of them at once with the domain's.
            failures.AddRange(await store(context, cancellationToken).ConfigureAwait(false));
            (aggregate as IHasDomainEvents)?.ClearDomainEvents();
            return failures;
        }
        catch (RaskValidationException refused)
        {
            return refused.Failures;
        }
        finally
        {
            Running.Value = outer;
        }
    }

    // The save's store refusals: its declared unique rules (with the tenant in flight standing for the stamp a new row has
    // not had yet) and its references to another tenant-scoped table.
    private static async Task<List<FieldFailure>> SaveRefusalsAsync(DbContext context, CancellationToken cancellationToken)
    {
        var failures = new List<FieldFailure>();

        if (UniqueRulesOfSave.Touched(context) is { Count: > 0 } touched)
        {
            failures.AddRange(await UniqueRulesOfSave.FailuresAsync(
                context, touched, AsWritten, row => RowPath.Of(context, row), cancellationToken).ConfigureAwait(false));
        }

        failures.AddRange(await ReferencedTenantCheck.FailuresAsync(context, cancellationToken).ConfigureAwait(false));
        return failures;
    }

    // A row's value as the save would write it: a new tenant-scoped row's tenant is the one in flight.
    private static object? AsWritten(EntityEntry row, string name)
    {
        var value = row.Property(name).CurrentValue;
        return value is null && string.Equals(name, Columns.TenantId, StringComparison.Ordinal) &&
               ConventionRegistry.IsPerTenant(row.Metadata.ClrType) && Tenant.Key is { } tenant && tenant != Tenant.Nobody
            ? TenantColumn.ValueFor(tenant, row.Property(name).Metadata.ClrType, row.Metadata.ClrType.Name)
            : value;
    }

    private sealed class Unheld : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
