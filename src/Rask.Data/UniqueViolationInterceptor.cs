using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Turns the violation of a unique index that carries a message (<c>HasViolationMessage</c>) into the
///     failure a validator produces, for every save through the context — Rask's own and plain EF Core's.
/// </summary>
/// <remarks>
///     Registered LAST by <c>AddRaskData</c>: it replaces the exception by throwing, and an interceptor after it
///     would not be told the save failed.
/// </remarks>
internal sealed class UniqueViolationInterceptor : SaveChangesInterceptor
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

        if (UniqueViolation.Translate(eventData.Context, eventData.Exception) is { } violation)
        {
            throw violation;
        }
    }
}
