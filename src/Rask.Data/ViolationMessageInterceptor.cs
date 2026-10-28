using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Turns a refusal of the database's that the model has a sentence for into the failure a validator
///     produces, for every save through the context — Rask's own and plain EF Core's: the violation of a unique
///     index (<c>IsUnique("…")</c>), and the delete of a row that is still referenced
///     (<c>OnDelete(behavior, "…")</c>, <c>WhenReferenced("…")</c>), and a row a form's list added whose key points
///     nowhere (<c>ActivityIds</c>).
/// </summary>
/// <remarks>
///     Registered LAST by <c>AddRaskData</c>: it replaces the exception by throwing, and an interceptor after it
///     would not be told the save failed.
/// </remarks>
internal sealed class ViolationMessageInterceptor : SaveChangesInterceptor
{
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

    private static void Translate(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var violation = UniqueViolation.Translate(eventData.Context, eventData.Exception)
            ?? ReferencedDelete.Translate(eventData.Exception)
            ?? HeldRowReference.Translate(eventData.Exception);

        if (violation is not null)
        {
            throw violation;
        }
    }
}
