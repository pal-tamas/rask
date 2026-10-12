using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     Says what a refusal of the database's means to the person who caused it: a unique index's violation,
///     and the delete of a row that is still referenced.
/// </summary>
/// <remarks>
///     <code>
/// builder.HasIndex(d =&gt; new { d.Name, d.TenantId }).IsUnique("A destination with this name already exists.");
///
/// await Destination.Named(model.Name).Save();   // nothing else at the call site
///     </code>
///     <para>
///         A save that violates the index then fails the way a validator's rule does — a
///         <see cref="Rask.Cqrs.RaskValidationException" /> carrying the message — rather than as the provider's
///         <c>DbUpdateException</c>, whose text names tables and holds the conflicting value. It is ONE
///         failure over every property the index names — the tenant aside — so a form shows the message under
///         each of those fields with nothing written in its submit handler.
///     </para>
///     <para>
///         The message is a constant, on purpose: it is shown to whoever sent the value, and on a table where
///         uniqueness is not per tenant the row it collided with may belong to somebody else.
///     </para>
///     <para>
///         An index with no message — EF Core's own <c>IsUnique()</c> — keeps the provider's error exactly as
///         it is.
///     </para>
///     <code>
/// builder.HasMany&lt;DeliveryNote&gt;().WithOne()
///     .HasForeignKey(n =&gt; n.TrailerId)
///     .OnDelete(DeleteBehavior.Restrict, "The trailer is on a delivery note and cannot be deleted.");
///
/// builder.WhenReferenced("The trailer is still in use and cannot be deleted.");   // any other reference
///
/// await Trailer.Delete(id);   // nothing else at the call site
///     </code>
///     <para>
///         A delete the database refuses because of that reference fails the same way, with ONE failure that
///         names no field: it is about the delete, not about anything on a form.
///     </para>
/// </remarks>
public static class ViolationMessageBuilderExtensions
{
    /// <summary>Makes the index unique, and says what a save that violates it fails with.</summary>
    /// <param name="index">The index.</param>
    /// <param name="message">What to tell the person whose value collided. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     EF Core's own <c>IsUnique()</c> and <c>IsUnique(false)</c> are untouched: only a string binds here.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static IndexBuilder IsUnique(this IndexBuilder index, string message)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        index.Metadata.SetAnnotation(UniqueViolation.Annotation, message);
        return index.IsUnique();
    }

    /// <summary>Makes the index unique, and says what a save that violates it fails with.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="message">What to tell the person whose value collided. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     EF Core's own <c>IsUnique()</c> and <c>IsUnique(false)</c> are untouched: only a string binds here.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static IndexBuilder<TEntity> IsUnique<TEntity>(this IndexBuilder<TEntity> index, string message)
    {
        IsUnique((IndexBuilder)index, message);
        return index;
    }

    /// <summary>
    ///     Says what happens to the rows that reference a deleted one, and what a delete the database refuses
    ///     because of them fails with.
    /// </summary>
    /// <param name="relationship">The relationship.</param>
    /// <param name="deleteBehavior">What EF Core's own <c>OnDelete</c> takes.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     EF Core's own <c>OnDelete(behavior)</c> is untouched: only a string binds here. The message is used
    ///     when the database refuses the delete, so it says something under <c>Restrict</c> and
    ///     <c>NoAction</c>; a behaviour that never refuses never shows it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static ReferenceCollectionBuilder OnDelete(
        this ReferenceCollectionBuilder relationship, DeleteBehavior deleteBehavior, string message)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        relationship.OnDelete(deleteBehavior).Metadata.SetAnnotation(ReferencedDelete.RelationshipAnnotation, message);
        return relationship;
    }

    /// <summary>
    ///     Says what happens to the rows that reference a deleted one, and what a delete the database refuses
    ///     because of them fails with.
    /// </summary>
    /// <typeparam name="TPrincipal">The entity that is referenced.</typeparam>
    /// <typeparam name="TDependent">The entity that references it.</typeparam>
    /// <param name="relationship">The relationship.</param>
    /// <param name="deleteBehavior">What EF Core's own <c>OnDelete</c> takes.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     EF Core's own <c>OnDelete(behavior)</c> is untouched: only a string binds here. The message is used
    ///     when the database refuses the delete, so it says something under <c>Restrict</c> and
    ///     <c>NoAction</c>; a behaviour that never refuses never shows it.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static ReferenceCollectionBuilder<TPrincipal, TDependent> OnDelete<TPrincipal, TDependent>(
        this ReferenceCollectionBuilder<TPrincipal, TDependent> relationship,
        DeleteBehavior deleteBehavior,
        string message)
        where TPrincipal : class
        where TDependent : class
    {
        OnDelete((ReferenceCollectionBuilder)relationship, deleteBehavior, message);
        return relationship;
    }

    /// <summary>
    ///     Says what happens to the row that references a deleted one, and what a delete the database refuses
    ///     because of it fails with.
    /// </summary>
    /// <param name="relationship">The one-to-one relationship.</param>
    /// <param name="deleteBehavior">What EF Core's own <c>OnDelete</c> takes.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>EF Core's own <c>OnDelete(behavior)</c> is untouched: only a string binds here.</remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static ReferenceReferenceBuilder OnDelete(
        this ReferenceReferenceBuilder relationship, DeleteBehavior deleteBehavior, string message)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        relationship.OnDelete(deleteBehavior).Metadata.SetAnnotation(ReferencedDelete.RelationshipAnnotation, message);
        return relationship;
    }

    /// <summary>
    ///     Says what happens to the row that references a deleted one, and what a delete the database refuses
    ///     because of it fails with.
    /// </summary>
    /// <typeparam name="TEntity">The entity on one end.</typeparam>
    /// <typeparam name="TRelatedEntity">The entity on the other.</typeparam>
    /// <param name="relationship">The one-to-one relationship.</param>
    /// <param name="deleteBehavior">What EF Core's own <c>OnDelete</c> takes.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>EF Core's own <c>OnDelete(behavior)</c> is untouched: only a string binds here.</remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static ReferenceReferenceBuilder<TEntity, TRelatedEntity> OnDelete<TEntity, TRelatedEntity>(
        this ReferenceReferenceBuilder<TEntity, TRelatedEntity> relationship,
        DeleteBehavior deleteBehavior,
        string message)
        where TEntity : class
        where TRelatedEntity : class
    {
        OnDelete((ReferenceReferenceBuilder)relationship, deleteBehavior, message);
        return relationship;
    }

    /// <summary>
    ///     Says what deleting a row of this entity fails with when the database refuses because something
    ///     still references it.
    /// </summary>
    /// <param name="entity">The entity whose rows are referenced.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     The sentence for every reference that has none of its own — and the only one there can be for a
    ///     table the model does not map: another context's, or one this app never reads. A relationship that
    ///     says its own with <c>OnDelete(behavior, "…")</c> is answered with that instead.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static EntityTypeBuilder WhenReferenced(this EntityTypeBuilder entity, string message)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        entity.Metadata.SetAnnotation(ReferencedDelete.EntityAnnotation, message);
        return entity;
    }

    /// <summary>
    ///     Says what deleting a row of this entity fails with when the database refuses because something
    ///     still references it.
    /// </summary>
    /// <typeparam name="TEntity">The entity whose rows are referenced.</typeparam>
    /// <param name="entity">The entity whose rows are referenced.</param>
    /// <param name="message">What to tell the person whose delete was refused. Shown as written.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     The sentence for every reference that has none of its own — and the only one there can be for a
    ///     table the model does not map: another context's, or one this app never reads. A relationship that
    ///     says its own with <c>OnDelete(behavior, "…")</c> is answered with that instead.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty or white space.</exception>
    public static EntityTypeBuilder<TEntity> WhenReferenced<TEntity>(this EntityTypeBuilder<TEntity> entity, string message)
        where TEntity : class
    {
        WhenReferenced((EntityTypeBuilder)entity, message);
        return entity;
    }
}
