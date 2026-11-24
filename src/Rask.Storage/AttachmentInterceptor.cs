using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
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
///     Makes an <see cref="Attachment" /> one write with its row, wherever the row holds it — on itself, on a child row,
///     inside a value object, in a list or a JSON column: stores a pending file as the row is saved, deletes a file
///     replaced, cleared or deleted with its row once the save has gone through, and takes a stored file back when the
///     save fails.
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

    // Whether a type holds an attachment anywhere, so a row that cannot is never walked.
    private static readonly ConcurrentDictionary<ITypeBase, bool> Holders = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not { } context)
        {
            return result;
        }

        if (Held(context).Any(static held => held.Attachment.IsPending))
        {
            throw new InvalidOperationException(
                "An Attachment still to be stored is saved with SaveChanges, which cannot store its file: save with " +
                "SaveChangesAsync — every generated operation does.");
        }

        Passes.AddOrUpdate(context, new Pass([], Released(context)));
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
            foreach (var held in Held(context).Where(static held => held.Attachment.IsPending).ToList())
            {
                field = held.Attachment.Field ?? held.Path;
                stored.Add((await held.Attachment.Store(cancellationToken).ConfigureAwait(false)).Id);
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

        // Stored in place: the values EF Core writes changed under it.
        context.ChangeTracker.DetectChanges();
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

    // Every attachment a tracked row holds now, with the form path it sits at (`Pistols[1].Documents[0]`).
    private static List<HeldAttachment> Held(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var found = new List<HeldAttachment>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Deleted && HoldsAttachments(entry.Metadata))
            {
                Walk(entry.Entity, entry.Metadata, RowPath.Of(context, entry), found);
            }
        }

        return found;
    }

    // The files this save lets go: those a row held when it was read and holds no more — replaced, cleared, or deleted
    // with the row. A soft-deleted row (it has a DeletedAt, whichever interceptor runs first) keeps its files.
    private static List<Guid> Released(DbContext context)
    {
        var released = new List<Guid>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted) || !HoldsAttachments(entry.Metadata))
            {
                continue;
            }

            var gone = entry.State == EntityState.Deleted && entry.Metadata.FindProperty(Columns.DeletedAt) is null;
            if (entry.State == EntityState.Deleted && !gone)
            {
                continue;
            }

            var read = new List<HeldAttachment>();
            Walk(entry.OriginalValues.ToObject(), entry.Metadata, "", read);

            var now = new List<HeldAttachment>();
            if (!gone)
            {
                Walk(entry.Entity, entry.Metadata, "", now);
            }

            var kept = now.Select(static h => h.Attachment.Id).ToHashSet();
            released.AddRange(read.Select(static h => h.Attachment.Id).Where(id => id != Guid.Empty && !kept.Contains(id)));
        }

        return released;
    }

    // The attachments `holder` holds through its complex members — a value object, a list of them — however deep.
    private static void Walk(object holder, ITypeBase type, string path, List<HeldAttachment> found)
    {
        foreach (var complex in type.GetComplexProperties())
        {
            if (!HoldsAttachments(complex.ComplexType) || complex.GetGetter().GetClrValue(holder) is not { } value)
            {
                continue;
            }

            var at = path + complex.Name;
            if (complex.IsCollection)
            {
                var index = 0;
                foreach (var element in (IEnumerable)value)
                {
                    Collect(element, complex.ComplexType, at + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", found);
                    index++;
                }
            }
            else
            {
                Collect(value, complex.ComplexType, at, found);
            }
        }
    }

    private static void Collect(object? value, ITypeBase type, string path, List<HeldAttachment> found)
    {
        if (value is Attachment attachment)
        {
            found.Add(new HeldAttachment(attachment, path));
        }
        else if (value is not null)
        {
            Walk(value, type, path + ".", found);
        }
    }

    private static bool HoldsAttachments(ITypeBase type) =>
        Holders.GetOrAdd(type, static t =>
            t.ClrType == typeof(Attachment) || t.GetComplexProperties().Any(static c => HoldsAttachments(c.ComplexType)));

    // After the save: inside a transaction the row goes with it and the bytes once it commits; outside, at once.
    private static async Task Release(Pass pass)
    {
        foreach (var id in pass.Let.Distinct())
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

    private readonly record struct HeldAttachment(Attachment Attachment, string Path);
}
