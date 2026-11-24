using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
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
    internal static async Task<IReadOnlyList<FieldFailure>> Of<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        Func<Task<TAggregate>> change, CancellationToken cancellationToken)
        where TAggregate : class, IAggregate
    {
        var outer = Running.Value;
        Running.Value = true;
        try
        {
            var aggregate = await change().ConfigureAwait(false);

            var context = Db.CreateContext();
            await using var contextScope = context.ConfigureAwait(false);
            await AggregateSave.StageAsync(context, aggregate, cancellationToken).ConfigureAwait(false);
            await DomainValidation.CheckAsync(context, cancellationToken).ConfigureAwait(false);
            (aggregate as IHasDomainEvents)?.ClearDomainEvents();
            return [];
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

    private sealed class Unheld : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
