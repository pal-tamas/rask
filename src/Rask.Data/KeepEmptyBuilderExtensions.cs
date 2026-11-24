using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.KeepEmpty(c =&gt; c.VatNumber)</c> — a text left empty is stored as <c>''</c>, never as <c>NULL</c>, and
///     a <c>NULL</c> already stored stays <c>NULL</c> until somebody fills it.
/// </summary>
public static class KeepEmptyBuilderExtensions
{
    /// <summary>The annotation that marks a text kept empty rather than cleared to <c>NULL</c>.</summary>
    internal const string Annotation = "Rask:KeepEmpty";

    /// <summary>
    ///     Keeps a text left empty as <c>''</c> instead of clearing it to <c>NULL</c>, so a table whose rows tell the
    ///     two apart — an older client that refuses <c>NULL</c>, a report that counts them — keeps doing so.
    /// </summary>
    /// <typeparam name="TEntity">The entity that holds the text.</typeparam>
    /// <typeparam name="TValue">A <see cref="string" />, or a value object that holds one text.</typeparam>
    /// <param name="entity">The entity's builder, in its <c>Configure</c>.</param>
    /// <param name="member">The text: <c>c =&gt; c.VatNumber</c>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         What it changes is the generated model's writes — <c>Create(model)</c>, <c>Update(id, model)</c> and a
    ///         form bound to the model — which read it from this call in the entity's own static <c>Configure</c>,
    ///         as they read <c>IsUnique</c>. A field left empty, or emptied, writes the value object holding
    ///         <c>''</c> (its rules are not asked of an empty text) where <c>CreateOptional</c> would write nothing;
    ///         a new row starts with <c>''</c>. The one exception is a row that holds <c>NULL</c> there and is saved
    ///         with the field still empty: it keeps its <c>NULL</c>, so a save that changes nothing writes nothing.
    ///     </para>
    ///     <para>
    ///         The column itself already keeps <c>''</c>: a stored empty text loads as the value object holding it,
    ///         past its rules, the way any stored value does.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The member is not stored as a text column.</exception>
    public static EntityTypeBuilder<TEntity> KeepEmpty<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TValue>(
        this EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, TValue?>> member)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(member);

        var property = entity.Property(member).Metadata;
        var stored = property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
        if (stored != typeof(string))
        {
            throw new InvalidOperationException(
                $"KeepEmpty keeps an empty text, but '{typeof(TEntity).Name}.{property.Name}' is stored as a " +
                $"{stored.Name}. Call it on a string, or on a value object that holds one text.");
        }

        property.SetAnnotation(Annotation, true);
        return entity;
    }
}
