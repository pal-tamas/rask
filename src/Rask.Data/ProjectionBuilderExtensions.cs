using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     A projection declared on the rows it is made from, in place of a save hook: when a save changes what it watches,
///     the rows raise its event in that save.
/// </summary>
/// <remarks>
///     <code>
/// public static void Configure(EntityTypeBuilder&lt;Fueling&gt; b) =&gt;
///     b.Projects(f =&gt; new { f.Moment, f.PistolId, f.Number },
///                changed =&gt; new FuelingsToNumber(changed.Values(f =&gt; f.PistolId)));
///     </code>
/// </remarks>
public static class ProjectionBuilderExtensions
{
    extension<T>(EntityTypeBuilder<T> builder)
        where T : class, IAggregate
    {
        /// <summary>
        ///     Raises <typeparamref name="TEvent" /> from every save that adds a <typeparamref name="T" />, removes one, or
        ///     changes a member <paramref name="watched" /> names — once per save, made by <paramref name="raise" /> from
        ///     the rows it changed, each with what it held before and holds after.
        /// </summary>
        /// <typeparam name="TEvent">The event the projection raises.</typeparam>
        /// <param name="watched">The members it follows: <c>f =&gt; f.Moment</c>, or several as <c>f =&gt; new { f.Moment, f.PistolId }</c>.</param>
        /// <param name="raise">The event, from the changed rows — or <see langword="null" /> to raise none for this save.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentException"><paramref name="watched" /> names something other than the row's own members.</exception>
        /// <exception cref="InvalidOperationException"><typeparamref name="T" /> is not an <c>Aggregate&lt;TId&gt;</c>.</exception>
        /// <remarks>
        ///     The event is raised exactly as one the aggregate raised itself, onto the first row the save changed: its
        ///     durable handlers' outbox rows are written in that same save, so they commit with the change or not at
        ///     all, and its in-memory handlers run after the commit — none after a rollback. A <paramref name="raise" />
        ///     that throws fails the save. Declaring the same event on the same row again replaces the first declaration.
        /// </remarks>
        public EntityTypeBuilder<T> Projects<TEvent>(Expression<Func<T, object?>> watched, Func<ProjectedRows<T>, TEvent?> raise)
            where TEvent : class, IEvent
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(watched);
            ArgumentNullException.ThrowIfNull(raise);

            if (!typeof(IProjectionCarrier).IsAssignableFrom(typeof(T)))
            {
                throw new InvalidOperationException(
                    $"b.Projects(…) raising {typeof(TEvent).Name} is declared on {typeof(T).Name}, which is not an Aggregate<TId>: " +
                    "a projection's event rides on its rows, as an aggregate's own events do.");
            }

            DeclaredProjections.Add(typeof(T), new DeclaredProjection<T, TEvent>(Members(watched, typeof(TEvent)), raise));
            return builder;
        }
    }

    // f => f.Moment, f => (object)f.Number, or f => new { f.Moment, f.PistolId }: the row's own members, by name.
    private static string[] Members<T>(Expression<Func<T, object?>> watched, Type eventType)
    {
        var body = Unboxed(watched.Body);
        var members = body is NewExpression created ? created.Arguments.Select(Unboxed).ToArray() : [body];
        if (members.Length == 0 || members.Any(m => m is not MemberExpression { Expression: ParameterExpression }))
        {
            throw new ArgumentException(
                $"b.Projects({watched}, …) raising {eventType.Name} watches the row's own members — f => f.Moment, or " +
                "f => new { f.Moment, f.PistolId } — not an expression over them.",
                nameof(watched));
        }

        return [.. members.Cast<MemberExpression>().Select(static m => m.Member.Name).Distinct(StringComparer.Ordinal)];
    }

    private static Expression Unboxed(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
            ? Unboxed(convert.Operand)
            : expression;
}
