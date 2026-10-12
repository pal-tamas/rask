using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     Stages a delete, and says the declared sentence when EF Core refuses it before the database is asked.
/// </summary>
/// <remarks>
///     A context that is already tracking a row which references the one being deleted, through a required
///     relationship that does not cascade, refuses in <c>Remove</c> itself — an
///     <see cref="InvalidOperationException" /> with no code to recognise it by. It is the same refusal the
///     database would have made, so it is answered with the same sentence, found from the rows that were
///     tracked rather than from the exception's text.
/// </remarks>
internal static class TrackedReferenceRefusal
{
    /// <summary>Marks <paramref name="entity" /> deleted in <paramref name="context" />.</summary>
    /// <exception cref="RaskValidationException">
    ///     A tracked row still references it, and the model says what that means.
    /// </exception>
    internal static void Remove(DbContext context, object entity)
    {
        // Looked up first: a refused Remove leaves the tracker part-way through the change.
        var tracked = TrackedReferences(context, context.Entry(entity));

        try
        {
            context.Remove(entity);
        }
        catch (InvalidOperationException refused) when (tracked.Count > 0)
        {
            if (Sentence(context.Entry(entity).Metadata, tracked) is { } failure)
            {
                throw new RaskValidationException([failure], refused);
            }

            throw;
        }
    }

    private static Wire.FieldFailure? Sentence(IEntityType deleted, List<IForeignKey> tracked)
    {
        var source = tracked is [var only] ? only.GetConstraintName() : deleted.ShortName();
        var sentence = tracked is [var one] ? ReferencedDelete.SentenceOf(one) : null;

        return (sentence ?? ReferencedDelete.GeneralSentenceOf(deleted)) is { } message
            ? ReferencedDelete.About(message, source)
            : null;
    }

    // The relationships EF Core severs on a delete and cannot — required, and not cascading — that have a
    // tracked row on the other end. Not scanned at all for an entity that declares no sentence.
    private static List<IForeignKey> TrackedReferences(DbContext context, EntityEntry deleted)
    {
        var severed = deleted.Metadata.GetReferencingForeignKeys().Where(Severs).ToList();
        if (severed.Count == 0 ||
            (ReferencedDelete.GeneralSentenceOf(deleted.Metadata) is null &&
             !severed.Exists(static key => ReferencedDelete.SentenceOf(key) is not null)))
        {
            return [];
        }

        var entries = context.ChangeTracker.Entries().Where(static entry => entry.State != EntityState.Deleted).ToList();
        return severed.FindAll(key => entries.Exists(entry => References(entry, key, deleted)));
    }

    private static bool Severs(IForeignKey key) =>
        key is { IsRequired: true, IsOwnership: false } &&
        key.DeleteBehavior is DeleteBehavior.Restrict or DeleteBehavior.NoAction
            or DeleteBehavior.ClientSetNull or DeleteBehavior.SetNull;

    private static bool References(EntityEntry dependent, IForeignKey key, EntityEntry principal)
    {
        if (!key.DeclaringEntityType.IsAssignableFrom(dependent.Metadata))
        {
            return false;
        }

        for (var i = 0; i < key.Properties.Count; i++)
        {
            var value = dependent.Property(key.Properties[i].Name).CurrentValue;
            if (!Equals(value, principal.Property(key.PrincipalKey.Properties[i].Name).CurrentValue))
            {
                return false;
            }
        }

        return true;
    }
}
