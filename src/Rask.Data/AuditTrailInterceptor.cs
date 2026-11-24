using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.Data;

/// <summary>
///     Adds to each save the <see cref="AuditEntry" /> of every <see cref="Audited" /> aggregate it writes
///     (<see cref="AuditTrail" />). Registered by
///     <see cref="RaskDataServiceCollectionExtensions.AddRaskData(Microsoft.Extensions.DependencyInjection.IServiceCollection)" />
///     after <see cref="AuditingInterceptor" />, so it records the rows as they go out: stamped, versioned, a soft delete
///     rewritten.
/// </summary>
/// <param name="timeProvider">Rask's clock.</param>
/// <param name="format">The app's <see cref="AuditFormat" />, or Rask's.</param>
internal sealed class AuditTrailInterceptor(TimeProvider timeProvider, AuditFormat format) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        AuditTrail.Saved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        AuditTrail.Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => AuditTrail.Failed(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        AuditTrail.Failed(eventData.Context);
        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData) => AuditTrail.Failed(eventData.Context);

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        AuditTrail.Failed(eventData.Context);
        return Task.CompletedTask;
    }

    private void Record(DbContextEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            AuditTrail.Record(context, format, timeProvider.GetUtcNow().UtcDateTime);
        }
    }
}
