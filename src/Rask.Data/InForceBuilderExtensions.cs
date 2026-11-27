using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.InForceWhile(f =&gt; f.UnfiledAt == null)</c> — when a child is the one in force, of those its aggregate
///     holds.
/// </summary>
public static class InForceBuilderExtensions
{
    /// <summary>The annotation that carries the declaration until the parent's index is made from it.</summary>
    internal const string Annotation = "Rask:InForce";

    /// <summary>
    ///     Says which of the child's rows are in force: of those its aggregate holds, the database keeps at most one.
    /// </summary>
    /// <typeparam name="TChild">The child, held in a collection of its aggregate.</typeparam>
    /// <param name="child">The child's builder, in its <c>Configure</c>.</param>
    /// <param name="inForce">
    ///     The rows in force: <c>f =&gt; f.UnfiledAt == null</c>. The plainest condition, as an index's <c>Where(…)</c>
    ///     takes: columns compared with constants or null, joined by <c>&amp;&amp;</c>.
    /// </param>
    /// <param name="message">What a save that would put a second one in force is refused with.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     The same condition is what <see cref="InForceExtensions.InForce{TChild}(IEnumerable{TChild})" /> asks of the
    ///     children in hand, so the aggregate and the index never disagree about which one it is.
    /// </remarks>
    public static EntityTypeBuilder<TChild> InForceWhile<[DynamicallyAccessedMembers(DataTrimming.Entity)] TChild>(
        this EntityTypeBuilder<TChild> child,
        Expression<Func<TChild, bool>> inForce,
        string message = "Another one is already in force.")
        where TChild : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(inForce);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        child.Metadata.SetAnnotation(Annotation, new InForceDeclaration(new DeclaredExpression(inForce, $"InForceWhile({inForce})"), message));
        InForceRules.Remember(inForce);
        return child;
    }
}
