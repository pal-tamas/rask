using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     Turns a refusal of the database's that the model has a sentence for into the failure a validator
///     produces, for every save through the context — Rask's own and plain EF Core's: the violation of a unique
///     index (<c>IsUnique("…")</c>), and the delete of a row that is still referenced
///     (<c>OnDelete(behavior, "…")</c>, <c>WhenReferenced("…")</c>), and a row a form's list added whose key points
///     nowhere (<c>ActivityIds</c>).
/// </summary>
/// <remarks>
///     <para>
///         It also tells the context's <see cref="ViolationLogging" /> when a save starts and ends, and — as a
///         command interceptor too, the same instance in both roles — which exception a failed command threw: that
///         is what keeps EF Core's own errors for exactly the refusal it translated out of the log.
///     </para>
///     <para>
///         Registered LAST by <c>AddRaskData</c>: it replaces the exception by throwing, and an interceptor after it
///         would not be told the save failed.
///     </para>
/// </remarks>
internal sealed class ViolationMessageInterceptor : SaveChangesInterceptor, IDbCommandInterceptor
{
    /// <summary>
    ///     The validation failure <paramref name="failure" /> stands for, when the model has a sentence for it;
    ///     otherwise null.
    /// </summary>
    internal static RaskValidationException? Translation(DbContext? context, Exception? failure) =>
        UniqueViolation.Translate(context, failure)
        ?? ReferencedDelete.Translate(failure)
        ?? HeldRowReference.Translate(failure);

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Starting(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Starting(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Ended(eventData);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Ended(eventData);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        Ended(eventData);
        base.SaveChangesCanceled(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Ended(eventData);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Translate(eventData);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Translate(eventData);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <inheritdoc />
    public void CommandFailed(DbCommand command, CommandErrorEventData eventData) => Failed(eventData);

    /// <inheritdoc />
    public Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(eventData);
        return Task.CompletedTask;
    }

    private static void Failed(CommandErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ViolationLogging.Of(eventData.Context)?.CommandFailed(eventData.Exception);
    }

    private static void Starting(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is { } context)
        {
            ViolationLogging.Of(context)?.SaveStarting(context);
        }
    }

    private static void Ended(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        ViolationLogging.Of(eventData.Context)?.SaveEnded(translated: null);
    }

    private static void Translate(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var logging = ViolationLogging.Of(eventData.Context);
        var violation = logging?.TranslationOf(eventData.Exception) ?? Translation(eventData.Context, eventData.Exception);
        logging?.SaveEnded(violation is null ? null : eventData.Exception);

        if (violation is not null)
        {
            throw violation;
        }
    }
}
