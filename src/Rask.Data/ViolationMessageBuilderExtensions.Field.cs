using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

public static partial class ViolationMessageBuilderExtensions
{
    /// <summary>Names the form field a violation of the unique index is shown under.</summary>
    /// <param name="index">The index.</param>
    /// <param name="field">
    ///     The field as the form's model names it — <c>Name</c>, <c>Km.Start</c> — in place of the properties the index
    ///     is over.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <code>
    /// builder.HasIndex(v =&gt; v.NormalizedPlate).IsUnique("A vehicle with this plate already exists.").Field("Plate");
    ///     </code>
    ///     For an index whose column is not what the form edits — a normalised copy, a computed key — or one over
    ///     several properties of which the form shows one. Without it the message goes under every property the index
    ///     names. A child row of an aggregate still carries its row: <c>Pistols[2].Plate</c>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="field" /> is null, empty or white space.</exception>
    public static IndexBuilder Field(this IndexBuilder index, string field)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        index.Metadata.SetAnnotation(UniqueViolation.FieldAnnotation, field);
        return index;
    }

    /// <summary>Names the form field a violation of the unique index is shown under.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="field">
    ///     The field as the form's model names it — <c>Name</c>, <c>Km.Start</c> — in place of the properties the index
    ///     is over.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="field" /> is null, empty or white space.</exception>
    public static IndexBuilder<TEntity> Field<TEntity>(this IndexBuilder<TEntity> index, string field)
    {
        Field((IndexBuilder)index, field);
        return index;
    }
}
