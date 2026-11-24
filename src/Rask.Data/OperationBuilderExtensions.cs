using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.Operation(s =&gt; s.SetOpening, roles: Roles.Admin)</c> — what an operation of an aggregate asks before it
///     runs.
/// </summary>
public static class OperationBuilderExtensions
{
    /// <summary>
    ///     Names who may run one operation of the aggregate: its generated entry — <c>await Supply.SetOpening(id,
    ///     version, opening)</c> — refuses anyone else with a <c>ForbiddenException</c>, before anything is read.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the operation belongs to.</typeparam>
    /// <param name="aggregate">The aggregate's builder, in its <c>Configure</c>.</param>
    /// <param name="operation">The public method the entry runs: <c>s =&gt; s.SetOpening</c>.</param>
    /// <param name="roles">
    ///     The roles that admit, comma-separated as <c>[Authorize(Roles = …)]</c> takes them: any one of them is
    ///     enough. A constant, because it is read where the entry is generated.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     Read from the source of the aggregate's own static <c>Configure</c> by the generator that writes the entry,
    ///     as <c>KeepEmpty</c> and <c>IsUnique</c> are, so the check costs nothing at run time and is there before any
    ///     model is built. The call itself only checks that it names a method of the aggregate. Only the generated
    ///     entry asks: the method called on an aggregate in hand is the caller's own code, and runs as written.
    /// </remarks>
    /// <param name="manyAggregates">
    ///     Why the operation writes more than one aggregate, a constant: the entry runs in
    ///     <c>Db.Transaction(…, manyAggregates: "…")</c> (<see cref="RaskDataOptions.OneAggregatePerTransaction" />).
    /// </param>
    /// <param name="forUpdate">
    ///     Locks the aggregate's row before the entry loads it, to the end of its transaction — so two operations on one
    ///     aggregate run one after the other rather than the second refused as stale.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="operation" /> names no method of the aggregate.</exception>
    public static EntityTypeBuilder<TAggregate> Operation<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        this EntityTypeBuilder<TAggregate> aggregate,
        Expression<Func<TAggregate, Delegate>> operation,
        string? roles = null,
        string? manyAggregates = null,
        bool forUpdate = false)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(operation);

        _ = MethodOf(operation);
        return aggregate;
    }

    /// <summary>
    ///     Names the child rows one operation locks before its entry loads the aggregate —
    ///     <c>b.Operation(s =&gt; s.Distribute, forUpdate: s =&gt; s.Parts)</c> — and, as the overload above, who may run
    ///     it and why it writes more than one aggregate.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the operation belongs to.</typeparam>
    /// <param name="aggregate">The aggregate's builder, in its <c>Configure</c>.</param>
    /// <param name="operation">The public method the entry runs: <c>s =&gt; s.Distribute</c>.</param>
    /// <param name="forUpdate">
    ///     The child collection whose rows are locked to the end of the entry's transaction: <c>s =&gt; s.Parts</c>. On
    ///     SQL Server the range is held too, so no part is added under it.
    /// </param>
    /// <param name="roles">The roles that admit, comma-separated; any one is enough. A constant.</param>
    /// <param name="manyAggregates">Why the operation writes more than one aggregate, a constant.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="operation" /> names no method of the aggregate, or <paramref name="forUpdate" /> no member.
    /// </exception>
    public static EntityTypeBuilder<TAggregate> Operation<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        this EntityTypeBuilder<TAggregate> aggregate,
        Expression<Func<TAggregate, Delegate>> operation,
        Expression<Func<TAggregate, IEnumerable<object>>> forUpdate,
        string? roles = null,
        string? manyAggregates = null)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(forUpdate);

        _ = MethodOf(operation);
        if ((forUpdate.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : forUpdate.Body)
            is not MemberExpression { Expression: ParameterExpression })
        {
            throw new ArgumentException(
                $"Name a child collection of {typeof(TAggregate).Name} the way Configure does: b.Operation(x => x.Distribute, forUpdate: x => x.Parts).",
                nameof(forUpdate));
        }

        return aggregate;
    }

    /// <summary>
    ///     Names who may run one operation or factory of the aggregate, by its name — the form a factory takes, whose
    ///     method group has two members once its entry is generated: <c>b.Operation(nameof(Register), roles: Roles.Admin)</c>.
    /// </summary>
    /// <typeparam name="TAggregate">The aggregate the operation belongs to.</typeparam>
    /// <param name="aggregate">The aggregate's builder, in its <c>Configure</c>.</param>
    /// <param name="operation">The method's name, a constant: <c>nameof(Register)</c>.</param>
    /// <param name="roles">The roles that admit, comma-separated; any one is enough. A constant.</param>
    /// <param name="manyAggregates">Why the factory writes more than one aggregate, a constant.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>Read from the source as the lambda form is; see <see cref="Operation{TAggregate}(EntityTypeBuilder{TAggregate}, Expression{Func{TAggregate, Delegate}}, string?, string?, bool)" />.</remarks>
    /// <exception cref="ArgumentException"><paramref name="operation" /> is empty.</exception>
    public static EntityTypeBuilder<TAggregate> Operation<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        this EntityTypeBuilder<TAggregate> aggregate, string operation, string? roles = null, string? manyAggregates = null)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        return aggregate;
    }

    // `s => s.Distribute` is a method group converted to a delegate: CreateDelegate called on the MethodInfo constant.
    private static MethodInfo MethodOf<TAggregate>(Expression<Func<TAggregate, Delegate>> operation)
    {
        var body = operation.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : operation.Body;

        return body is MethodCallExpression { Object: ConstantExpression { Value: MethodInfo method } } &&
               method.DeclaringType?.IsAssignableFrom(typeof(TAggregate)) == true
            ? method
            : throw new ArgumentException(
                $"Name a method of {typeof(TAggregate).Name} the way Configure does: b.Operation(x => x.Distribute, roles: \"Admin\").",
                nameof(operation));
    }
}
