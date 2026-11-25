using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

public static partial class ViolationMessageBuilderExtensions
{
    /// <summary>
    ///     Says what a save is refused with when this reference names a row the tenant in flight has no such row of —
    ///     none at all, or another tenant's.
    /// </summary>
    /// <param name="relationship">The relationship.</param>
    /// <param name="message">What to tell the person who chose the row, under the reference's field. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <code>
    /// builder.HasOne&lt;Company&gt;().WithMany().HasForeignKey(v =&gt; v.CompanyId).WhenMissing("No such company.");
    ///     </code>
    ///     A save of a <see cref="PerTenant" /> row checks every reference it sets to another tenant's table, so a
    ///     method needs no <c>Refuse.When(…)</c> of its own for it. Without a sentence here the referenced entity's
    ///     <c>WhenMissing("…")</c> answers, and without that the strings catalog's <c>ValidationNotFound</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static ReferenceCollectionBuilder WhenMissing(this ReferenceCollectionBuilder relationship, string message)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        relationship.Metadata.SetAnnotation(ReferencedTenantCheck.RelationshipAnnotation, message);
        return relationship;
    }

    /// <inheritdoc cref="WhenMissing(ReferenceCollectionBuilder, string)" />
    /// <typeparam name="TPrincipal">The entity that is referenced.</typeparam>
    /// <typeparam name="TDependent">The entity that references it.</typeparam>
    public static ReferenceCollectionBuilder<TPrincipal, TDependent> WhenMissing<TPrincipal, TDependent>(
        this ReferenceCollectionBuilder<TPrincipal, TDependent> relationship, string message)
        where TPrincipal : class
        where TDependent : class
    {
        WhenMissing((ReferenceCollectionBuilder)relationship, message);
        return relationship;
    }

    /// <inheritdoc cref="WhenMissing(ReferenceCollectionBuilder, string)" />
    public static ReferenceReferenceBuilder WhenMissing(this ReferenceReferenceBuilder relationship, string message)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        relationship.Metadata.SetAnnotation(ReferencedTenantCheck.RelationshipAnnotation, message);
        return relationship;
    }

    /// <inheritdoc cref="WhenMissing(ReferenceCollectionBuilder, string)" />
    /// <typeparam name="TEntity">The entity on one end.</typeparam>
    /// <typeparam name="TRelatedEntity">The entity on the other.</typeparam>
    public static ReferenceReferenceBuilder<TEntity, TRelatedEntity> WhenMissing<TEntity, TRelatedEntity>(
        this ReferenceReferenceBuilder<TEntity, TRelatedEntity> relationship, string message)
        where TEntity : class
        where TRelatedEntity : class
    {
        WhenMissing((ReferenceReferenceBuilder)relationship, message);
        return relationship;
    }

    /// <summary>
    ///     Says what a save is refused with when a reference to this entity names a row the tenant in flight has no
    ///     such row of — for every reference that says no sentence of its own.
    /// </summary>
    /// <param name="entity">The entity whose rows are referenced.</param>
    /// <param name="message">What to tell the person who chose the row. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <code>
    /// builder.WhenMissing("No such company.");   // in Company's Configure: every reference to a company
    ///     </code>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static EntityTypeBuilder WhenMissing(this EntityTypeBuilder entity, string message)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        entity.Metadata.SetAnnotation(ReferencedTenantCheck.EntityAnnotation, message);
        return entity;
    }

    /// <inheritdoc cref="WhenMissing(EntityTypeBuilder, string)" />
    /// <typeparam name="TEntity">The entity whose rows are referenced.</typeparam>
    public static EntityTypeBuilder<TEntity> WhenMissing<TEntity>(this EntityTypeBuilder<TEntity> entity, string message)
        where TEntity : class
    {
        WhenMissing((EntityTypeBuilder)entity, message);
        return entity;
    }
}
