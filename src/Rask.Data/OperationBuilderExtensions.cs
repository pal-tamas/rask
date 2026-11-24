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
    /// <exception cref="ArgumentException"><paramref name="operation" /> names no method of the aggregate.</exception>
    public static EntityTypeBuilder<TAggregate> Operation<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        this EntityTypeBuilder<TAggregate> aggregate, Expression<Func<TAggregate, Delegate>> operation, string? roles = null)
        where TAggregate : class, IAggregate
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(operation);

        _ = MethodOf(operation);
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
    /// <returns>The same builder.</returns>
    /// <remarks>Read from the source as the lambda form is; see <see cref="Operation{TAggregate}(EntityTypeBuilder{TAggregate}, Expression{Func{TAggregate, Delegate}}, string?)" />.</remarks>
    /// <exception cref="ArgumentException"><paramref name="operation" /> is empty.</exception>
    public static EntityTypeBuilder<TAggregate> Operation<[DynamicallyAccessedMembers(DataTrimming.Entity)] TAggregate>(
        this EntityTypeBuilder<TAggregate> aggregate, string operation, string? roles = null)
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
