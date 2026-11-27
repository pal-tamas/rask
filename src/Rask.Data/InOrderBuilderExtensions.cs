using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.InOrder(s =&gt; s.Parts)</c> — the children of a <see cref="HasMany{TChild}" /> keep their order: each child's
///     place is stored (<see cref="Columns.Position" />) and they load back in it.
/// </summary>
public static class InOrderBuilderExtensions
{
    /// <summary>The annotation that marks a <see cref="HasMany{TChild}" /> navigation whose order is kept.</summary>
    internal const string Annotation = "Rask:InOrder";

    /// <summary>
    ///     Keeps the order of <paramref name="children" />: the order the aggregate last gave them — by <c>Add</c>, or by
    ///     the specs of a <c>Sync</c> — is the order they load back in, whatever order the store returns their rows.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate that holds the children.</typeparam>
    /// <typeparam name="TChild">The child.</typeparam>
    /// <param name="aggregate">The aggregate's builder, in its <c>Configure</c>.</param>
    /// <param name="children">The children: <c>s =&gt; s.Parts</c>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     It adds a column to the child's table — an <c>int</c> named <see cref="Columns.Position" />, a shadow property
    ///     written on every save — so declaring it on children already stored is a migration. A child that declares a
    ///     <c>Position</c> of its own keeps it, and its order is then its own to keep.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="children" /> does not name a property of the aggregate.</exception>
    public static EntityTypeBuilder<TAggregate> InOrder<
        [DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TChild>(
        this EntityTypeBuilder<TAggregate> aggregate, Expression<Func<TAggregate, HasMany<TChild>>> children)
        where TAggregate : class
        where TChild : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(children);

        if (children.Body is not MemberExpression { Expression: ParameterExpression } member)
        {
            throw new ArgumentException(
                $"'{nameof(children)}' must name a HasMany property of {typeof(TAggregate).Name}, as in x => x.Parts, but was '{children}'.",
                nameof(children));
        }

        var navigation = (IMutableNavigation)aggregate.Navigation(member.Member.Name).Metadata;
        navigation.SetAnnotation(Annotation, true);

        // A child that names a member of its own Position keeps it: the order is then the child's.
        var child = navigation.TargetEntityType;
        if (child.ClrType.GetProperty(Columns.Position) is null && child.FindProperty(Columns.Position) is null)
        {
            child.AddProperty(Columns.Position, typeof(int));
        }

        return aggregate;
    }

    /// <summary>Whether <paramref name="navigation" /> is one <see cref="InOrder{TAggregate, TChild}" /> keeps the order of.</summary>
    internal static bool IsInOrder(this INavigation navigation) => navigation.FindAnnotation(Annotation)?.Value is true;
}
