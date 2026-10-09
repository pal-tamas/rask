using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     Says what a unique index's violation means to the person who caused it.
/// </summary>
/// <remarks>
///     <code>
/// builder.HasIndex(d =&gt; new { d.Name, d.TenantId }).IsUnique()
///     .HasViolationMessage("A destination with this name already exists.");
///
/// await Destination.Named(model.Name).Save();   // nothing else at the call site
///     </code>
///     <para>
///         A save that violates the index then fails the way a validator's rule does — a
///         <see cref="Rask.Cqrs.RaskValidationException" /> carrying the message — rather than as the provider's
///         <c>DbUpdateException</c>, whose text names tables and holds the conflicting value. The message is
///         filed under the property the index is over when that is ONE property beside the tenant, and under
///         the empty key — the request as a whole — when it is several.
///     </para>
///     <para>
///         The message is a constant, on purpose: it is shown to whoever sent the value, and on a table where
///         uniqueness is not per tenant the row it collided with may belong to somebody else.
///     </para>
///     <para>
///         An index with no message keeps the provider's error exactly as it is.
///     </para>
/// </remarks>
public static class ViolationMessageBuilderExtensions
{
    /// <summary>Sets the message a save that violates this unique index fails with.</summary>
    /// <param name="index">The index — a unique one; on any other the message is never used.</param>
    /// <param name="message">What to tell the person whose value collided. Shown as written.</param>
    /// <returns>The same builder.</returns>
    public static IndexBuilder HasViolationMessage(this IndexBuilder index, string message)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        index.Metadata.SetAnnotation(UniqueViolation.Annotation, message);
        return index;
    }

    /// <summary>Sets the message a save that violates this unique index fails with.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index — a unique one; on any other the message is never used.</param>
    /// <param name="message">What to tell the person whose value collided. Shown as written.</param>
    /// <returns>The same builder.</returns>
    public static IndexBuilder<TEntity> HasViolationMessage<TEntity>(this IndexBuilder<TEntity> index, string message)
    {
        HasViolationMessage((IndexBuilder)index, message);
        return index;
    }
}
