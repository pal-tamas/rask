using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;
using Rask.Data;
using Rask.Wire;

namespace Rask.Storage;

/// <summary>
///     Makes an <see cref="Attachment" /> one write with its row: stores a pending file as the row is saved, deletes a
///     file replaced, cleared or deleted with its row once the save has gone through, and takes a stored file back
///     when the save fails.
/// </summary>
/// <remarks>
///     Inside <c>Db.Transaction</c> — every generated operation — the store and the delete take part in the
///     transaction as <see cref="IFiles" /> always does: the row of a new file commits with the work and its bytes go
///     on a rollback; a deleted file's bytes go only once the work commits. Outside one, a failed save deletes what it
///     stored itself.
/// </remarks>
internal sealed class AttachmentInterceptor : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<DbContext, Pass> Passes = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && Held(context).Any(static held => held.Current?.IsPending == true))
        {
            throw new InvalidOperationException(
                "An Attachment still to be stored is saved with SaveChanges, which cannot store its file: save with " +
                "SaveChangesAsync — every generated operation does.");
        }

        if (eventData.Context is { } released)
        {
            Passes.AddOrUpdate(released, new Pass([], Released(released)));
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not { } context)
        {
            return result;
        }

        var stored = new List<Guid>();
        Passes.AddOrUpdate(context, new Pass(stored, Released(context)));

        var field = "";
        try
        {
            foreach (var held in Held(context).Where(static held => held.Current?.IsPending == true).ToList())
            {
                field = held.Property.Name;
                var (attachment, file) = await held.Current!.Store(cancellationToken).ConfigureAwait(false);
                stored.Add(file.Id);
                held.Entry.ComplexProperty(held.Property).CurrentValue = attachment;
            }
        }
        catch (Exception failed)
        {
            // A save stopped here is never told it failed: what this one stored goes now.
            Passes.Remove(context);
            await Forget(stored).ConfigureAwait(false);
            if (failed is FileRejectedException refused)
            {
                throw new RaskValidationException([new FieldFailure(refused.Message, [field])], refused);
            }

            throw;
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && Passes.TryGetValue(context, out var pass))
        {
            Passes.Remove(context);
            Release(pass).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && Passes.TryGetValue(context, out var pass))
        {
            Passes.Remove(context);
            await Release(pass).ConfigureAwait(false);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && Passes.TryGetValue(context, out var pass))
        {
            Passes.Remove(context);
            Forget(pass.Stored).GetAwaiter().GetResult();
        }
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && Passes.TryGetValue(context, out var pass))
        {
            Passes.Remove(context);
            await Forget(pass.Stored).ConfigureAwait(false);
        }
    }

    // Every attachment a tracked row holds on itself, as it stands now and as it was read.
    private static IEnumerable<HeldAttachment> Held(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            foreach (var property in entry.Metadata.GetComplexProperties())
            {
                if (!property.IsCollection && property.ComplexType.ClrType == typeof(Attachment))
                {
                    yield return new HeldAttachment(entry, property, entry.ComplexProperty(property).CurrentValue as Attachment);
                }
            }
        }
    }

    // The files this save lets go: the one a row held when it was read, now replaced, cleared, or deleted with the row.
    private static List<Guid> Released(DbContext context)
    {
        var released = new List<Guid>();
        foreach (var held in Held(context))
        {
            if (held.Entry.State is not (EntityState.Modified or EntityState.Deleted) ||
                held.Entry.ComplexProperty(held.Property).Property(nameof(Attachment.Id)).OriginalValue is not Guid read ||
                read == Guid.Empty)
            {
                continue;
            }

            // A row deleted for good lets its file go; one soft deleted (it has a DeletedAt, whichever interceptor runs
            // first) keeps it, as it keeps every other column.
            var gone = held.Entry.State == EntityState.Deleted && held.Entry.Metadata.FindProperty(Columns.DeletedAt) is null;
            if (gone || (held.Entry.State == EntityState.Modified && held.Current?.Id != read))
            {
                released.Add(read);
            }
        }

        return released;
    }

    // After the save: inside a transaction the row goes with it and the bytes once it commits; outside, at once.
    private static async Task Release(Pass pass)
    {
        foreach (var id in pass.Let)
        {
            await Files.Resolve().Delete(id).ConfigureAwait(false);
        }
    }

    // A save that failed outside a transaction keeps nothing it stored; inside one, the rollback takes it back.
    private static async Task Forget(List<Guid> stored)
    {
        if (AmbientTransaction.Current is not null)
        {
            return;
        }

        foreach (var id in stored)
        {
            await Files.Resolve().Delete(id).ConfigureAwait(false);
        }
    }

    private sealed record Pass(List<Guid> Stored, List<Guid> Let);

    private readonly record struct HeldAttachment(EntityEntry Entry, IComplexProperty Property, Attachment? Current);
}
