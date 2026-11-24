using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     Carries columns in an index without ordering by them, so a query that reads them is answered from the index
///     alone: <c>builder.HasIndex(p =&gt; p.PayeeId).Covering(p =&gt; new { p.Amount })</c>.
/// </summary>
/// <remarks>
///     <para>
///         One sentence for every database. On SQL Server it is written <c>INCLUDE ([Amount])</c> — EF Core's own
///         <c>IncludeProperties</c> — and on PostgreSQL <c>INCLUDE ("Amount")</c>. SQLite has no included columns, so
///         there the index is the key columns alone, and nothing about it reaches the model or a migration.
///     </para>
///     <para>
///         Read as the model is finalized, by every <see cref="RaskDbContext" />: a covered member that is not a mapped
///         property, or that is already one of the index's keys, fails the model build with its name.
///     </para>
/// </remarks>
public static class CoveringIndexBuilderExtensions
{
    /// <summary>Carries <paramref name="columns" /> in the index, beside its keys.</summary>
    /// <typeparam name="TEntity">The entity the index is on.</typeparam>
    /// <param name="index">The index.</param>
    /// <param name="columns">
    ///     The members to carry: one, <c>p =&gt; p.Amount</c>, or several, <c>p =&gt; new { p.Amount, p.Note }</c>. A value
    ///     object of several members stored in columns of its own is carried by all of them (<c>l =&gt; l.Km</c>) or one
    ///     (<c>l =&gt; l.Km.Start</c>).
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="columns" /> names anything but the entity's own members.</exception>
    public static IndexBuilder<TEntity> Covering<TEntity>(this IndexBuilder<TEntity> index, Expression<Func<TEntity, object?>> columns)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(columns);

        Covering((IndexBuilder)index, [.. Paths(columns)]);
        return index;
    }

    /// <summary>Carries the properties <paramref name="propertyNames" /> names in the index, beside its keys.</summary>
    /// <param name="index">The index.</param>
    /// <param name="propertyNames">
    ///     The properties to carry, by their names in the model; a member of a value object by its path, <c>"Km.Start"</c>.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException">No name is given, or one is empty.</exception>
    public static IndexBuilder Covering(this IndexBuilder index, params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (propertyNames.Length == 0 || Array.Exists(propertyNames, string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Name at least one property to carry, and no empty one: Covering(p => new { p.Amount }).", nameof(propertyNames));
        }

        index.Metadata.SetAnnotation(CoveringIndexConvention.Annotation, propertyNames);
        return index;
    }

    // `p => p.Amount`, `p => new { p.Amount, p.Km.Start }`: each member chain read from the entity, as a dotted path.
    private static List<string> Paths<TEntity>(Expression<Func<TEntity, object?>> columns)
    {
        var body = columns.Body is UnaryExpression { NodeType: ExpressionType.Convert } boxed ? boxed.Operand : columns.Body;
        var paths = new List<string>();
        foreach (var member in body is NewExpression created ? created.Arguments : [body])
        {
            paths.Add(PathOf(member, columns.Parameters[0]) ?? throw new ArgumentException(
                $"'{columns}' names something other than the entity's own members: write p => p.Amount, "
                + "p => new { p.Amount, p.Note } or p => p.Km.Start.", nameof(columns)));
        }

        return paths;
    }

    private static string? PathOf(Expression member, ParameterExpression entity)
    {
        var names = new Stack<string>();
        while (member is MemberExpression { Member: var named } access)
        {
            names.Push(named.Name);
            member = access.Expression!;
        }

        return member == entity && names.Count > 0 ? string.Join('.', names) : null;
    }
}
