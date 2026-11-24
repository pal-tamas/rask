using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>Names the columns of a value object of several members after a pattern.</summary>
public static class ValueObjectColumnsBuilderExtensions
{
    private const string MemberPlaceholder = "{Member}";

    /// <summary>
    ///     Names each column of the value object <paramref name="member" /> holds after <paramref name="pattern" />:
    ///     <c>b.Columns(n =&gt; n.Km, "{Member}Km")</c> stores a <c>KmRange(int Start, int End)</c> in <c>StartKm</c> and
    ///     <c>EndKm</c>, where EF Core would name them <c>Km_Start</c> and <c>Km_End</c>.
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TValue">The value object's type.</typeparam>
    /// <param name="builder">The entity's builder, in its <c>Configure</c>.</param>
    /// <param name="member">The entity's property that holds the value object: <c>n =&gt; n.Km</c>.</param>
    /// <param name="pattern">The column name, with <c>{Member}</c> where the value object's member name goes.</param>
    /// <returns>The same builder, to chain.</returns>
    /// <remarks>
    ///     <para>
    ///         For a table whose columns were named before the value object existed: the reports and the raw SQL that
    ///         read <c>StartKm</c> keep working, and no migration renames a column. The entity's read face follows the
    ///         same names — <c>DeliveryNoteRead.StartKm</c>, not <c>KmStart</c> — so a list filters and sorts by them.
    ///         The generator reads the call from the entity's own <c>Configure</c>, so write it there, with the pattern
    ///         as a constant.
    ///     </para>
    ///     <para>
    ///         A value object nested inside it keeps EF Core's own naming below the pattern: the pattern names it, then
    ///         <c>_</c> and each of its members (<c>"{Member}Km"</c> over a <c>Reading</c> member holding <c>Value</c>
    ///         and <c>Unit</c> is <c>ReadingKm_Value</c> and <c>ReadingKm_Unit</c>).
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    ///     <paramref name="member" /> is not a property of the entity, or <paramref name="pattern" /> has no
    ///     <c>{Member}</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The property is not stored as a value object of several members.</exception>
    public static EntityTypeBuilder<TEntity> Columns<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TValue>(
        this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TValue>> member, string pattern)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (!pattern.Contains(MemberPlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The pattern '{pattern}' has no {MemberPlaceholder}, so every column would get the same name: write "
                + $"it where the value object's member name goes, as in \"{MemberPlaceholder}Km\".",
                nameof(pattern));
        }

        var name = PropertyName(member);
        var complex = builder.Metadata.FindComplexProperty(name) ?? throw new InvalidOperationException(
            $"'{typeof(TEntity).Name}.{name}' is not stored as a value object of several members, so it has no columns "
            + "to name after a pattern. A value object of one value is one column: name it with HasColumnName.");

        Name(complex.ComplexType, pattern, nested: null);
        return builder;
    }

    private static void Name(IMutableComplexType type, string pattern, string? nested)
    {
        foreach (var property in type.GetDeclaredProperties())
        {
            property.SetColumnName(ColumnOf(pattern, nested, property.Name));
        }

        foreach (var inner in type.GetDeclaredComplexProperties())
        {
            Name(inner.ComplexType, pattern, ColumnOf(pattern, nested, inner.Name));
        }
    }

    // The first level takes the pattern; below it, EF Core's own Outer_Inner.
    private static string ColumnOf(string pattern, string? nested, string name) =>
        nested is null ? pattern.Replace(MemberPlaceholder, name, StringComparison.Ordinal) : nested + "_" + name;

    private static string PropertyName(LambdaExpression member) =>
        member.Body is MemberExpression { Member: var property } access && access.Expression == member.Parameters[0]
            ? property.Name
            : throw new ArgumentException(
                $"'{member}' is not a property of the entity: name the one that holds the value object, as in n => n.Km.",
                nameof(member));
}
