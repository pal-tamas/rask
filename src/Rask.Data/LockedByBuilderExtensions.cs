using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     Declares that a row is closed to writes while the row it belongs to says so — a delivery note of a closed period,
///     a transaction of a filed return — and every save holds to it, whatever page, job or import wrote the row.
/// </summary>
/// <remarks>
///     <code>
/// public static void Configure(EntityTypeBuilder&lt;DeliveryNote&gt; b) =&gt;
///     b.LockedBy(n =&gt; n.Period, (Period p) =&gt; p.Closed, Texts.PeriodClosed);
///     </code>
///     <para>
///         Asked by the save's domain step, with the rest of the domain's rules, before anything is written. For each
///         row of the type the save adds, changes or deletes — and for an aggregate whose child it adds, changes or
///         removes — the parent the row held when it was read AND the one it holds now are read, so a row is moved
///         neither out of a locked parent nor into one. A parent key that is null names no parent and locks nothing.
///     </para>
///     <para>
///         A locked parent refuses the save with one <see cref="Rask.Cqrs.RaskValidationException" />: under the parent
///         field when the save writes the row's parent (a new row, a moved one), and about the whole row otherwise. The
///         parents are read past every query filter: a soft-deleted closed period still holds its notes.
///     </para>
/// </remarks>
public static class LockedByBuilderExtensions
{
    /// <summary>Refuses every write of a <typeparamref name="T" /> whose parent is <paramref name="locked" />.</summary>
    /// <typeparam name="T">The row that is locked.</typeparam>
    /// <typeparam name="TParent">The row whose state locks it.</typeparam>
    /// <typeparam name="TParentId">The parent's key, as the row holds it: a typed id, an <c>int</c>, a nullable one.</typeparam>
    /// <param name="builder">The row's entity builder.</param>
    /// <param name="parent">The row's property holding its parent's key: <c>n =&gt; n.Period</c>.</param>
    /// <param name="locked">When the parent locks its rows: <c>(Period p) =&gt; p.Closed</c>, translated to the database's SQL.</param>
    /// <param name="message">What the refused save says. Shown as written.</param>
    /// <param name="forUpdate">
    ///     Reads the parents with an update lock held to the end of the save's transaction — <c>UPDLOCK, HOLDLOCK</c> on SQL
    ///     Server, <c>FOR UPDATE</c> on PostgreSQL; SQLite's one writer serializes the save already — so a parent cannot be
    ///     locked between the check and the write. The save then runs in a transaction of its own when the caller opened
    ///     none.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="parent" /> names no property of the row, or <paramref name="message" /> is blank.</exception>
    public static EntityTypeBuilder<T> LockedBy<T, [DynamicallyAccessedMembers(DataTrimming.Entity)] TParent, TParentId>(
        this EntityTypeBuilder<T> builder,
        Expression<Func<T, TParentId?>> parent,
        Expression<Func<TParent, bool>> locked,
        string message,
        bool forUpdate = false)
        where T : class
        where TParent : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return builder.LockedBy(parent, locked, _ => message, forUpdate);
    }

    /// <summary>
    ///     Refuses every write of a <typeparamref name="T" /> whose parent is <paramref name="locked" />, saying so in words
    ///     made from the parent: <c>p =&gt; $"The {p.Name} period is closed."</c>.
    /// </summary>
    /// <typeparam name="T">The row that is locked.</typeparam>
    /// <typeparam name="TParent">The row whose state locks it.</typeparam>
    /// <typeparam name="TParentId">The parent's key, as the row holds it: a typed id, an <c>int</c>, a nullable one.</typeparam>
    /// <param name="builder">The row's entity builder.</param>
    /// <param name="parent">The row's property holding its parent's key: <c>n =&gt; n.Period</c>.</param>
    /// <param name="locked">When the parent locks its rows: <c>(Period p) =&gt; p.Closed</c>, translated to the database's SQL.</param>
    /// <param name="message">What the refused save says, from the locked parent as it is stored.</param>
    /// <param name="forUpdate">Reads the parents with an update lock, as the other overload's <c>forUpdate</c> does.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="parent" /> names no property of the row.</exception>
    public static EntityTypeBuilder<T> LockedBy<T, [DynamicallyAccessedMembers(DataTrimming.Entity)] TParent, TParentId>(
        this EntityTypeBuilder<T> builder,
        Expression<Func<T, TParentId?>> parent,
        Expression<Func<TParent, bool>> locked,
        Func<TParent, string> message,
        bool forUpdate = false)
        where T : class
        where TParent : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(locked);
        ArgumentNullException.ThrowIfNull(message);

        ParentLocks.Add(typeof(T), new ParentLock<TParent>(PropertyOf(parent), locked, message, forUpdate));
        return builder;
    }

    // n => n.Period, seen through the conversion a nullable or boxed key puts around it.
    private static string PropertyOf<T, TParentId>(Expression<Func<T, TParentId?>> parent)
    {
        var body = parent.Body is UnaryExpression { NodeType: ExpressionType.Convert } cast ? cast.Operand : parent.Body;
        return body is MemberExpression { Expression: ParameterExpression } member
            ? member.Member.Name
            : throw new ArgumentException(
                $"'{nameof(parent)}' must name a property of {typeof(T).Name} directly, as in n => n.Period, but was '{parent}'.",
                nameof(parent));
    }
}
