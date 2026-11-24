using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.Gated(d =&gt; d.Card, Module.Tachograph)</c> — a member only the work a gate is open for may write: with the gate
///     shut, a save keeps the value it holds.
/// </summary>
public static class GatedBuilderExtensions
{
    /// <summary>
    ///     Gates an optional member: while <paramref name="gate" /> is shut for the work in flight, a save leaves the
    ///     stored value as it is, and a new row stores none.
    /// </summary>
    /// <typeparam name="TEntity">The entity that holds the member.</typeparam>
    /// <typeparam name="TValue">The member's type: an optional value, value object or several-value value object.</typeparam>
    /// <typeparam name="TGate">The app's own kind of gate — a module, a licence, a feature.</typeparam>
    /// <param name="entity">The entity's builder, in its <c>Configure</c>.</param>
    /// <param name="member">The member: <c>d =&gt; d.Card</c>.</param>
    /// <param name="gate">The gate it is behind: <c>Module.Tachograph</c>.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Whether a gate is open is the app's to say, once, with
    ///         <see cref="RaskDataServiceCollectionExtensions.AddRaskGate{TGate}" />. Every write path asks — a
    ///         generated <c>Update(id, model)</c>, an operation, an import — so a screen that does not show the field,
    ///         to someone without the module, never clears what somebody with it stored.
    ///     </para>
    ///     <para>
    ///         Only an optional member can be gated: a row added with the gate shut has nothing to store there.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The member is required, or is not one the entity stores.</exception>
    public static EntityTypeBuilder<TEntity> Gated<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TValue, TGate>(
        this EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, TValue?>> member, TGate gate)
        where TEntity : class
        where TGate : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(gate);

        var name = (member.Body as MemberExpression)?.Member.Name ?? throw new ArgumentException(
            $"Name one member of {typeof(TEntity).Name}: b.Gated(x => x.Card, gate).", nameof(member));

        var metadata = entity.Metadata;
        var nullable = metadata.FindProperty(name)?.IsNullable ?? metadata.FindComplexProperty(name)?.IsNullable ??
            throw new InvalidOperationException(
                $"Gated names '{typeof(TEntity).Name}.{name}', which the entity does not store.");

        if (!nullable)
        {
            throw new InvalidOperationException(
                $"Gated keeps '{typeof(TEntity).Name}.{name}' unwritten while its gate is shut, so a row added then has " +
                "nothing to store there: make the member optional.");
        }

        GatedMembers.Remember(typeof(TEntity), name, gate);
        return entity;
    }
}
