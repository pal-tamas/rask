using System.ComponentModel;
using System.Linq.Expressions;
using FluentValidation;

namespace Rask;

/// <summary>
///     How a generated form model borrows the rules its aggregate or entity states: which of its members carries
///     which of the aggregate's, and which lists hold rows of a child. Not an API to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ModelProjection
{
    private protected ModelProjection(Type owner) => Owner = owner;

    /// <summary>The aggregate or entity whose rules are borrowed.</summary>
    internal Type Owner { get; }

    /// <summary>The model's members, each with the member of the owner whose rules it carries.</summary>
    internal Dictionary<string, string> Members { get; } = new(StringComparer.Ordinal);

    /// <summary>The types of the rows the model's lists hold.</summary>
    internal List<Type> RowTypes { get; } = [];

    /// <summary>The model's validator: the owner's rules on the members that carry them, and each row's own.</summary>
    internal abstract IValidator Build(IReadOnlyList<IValidator> owner, IServiceProvider? services);
}

/// <summary>
///     How the generated <typeparamref name="TModel" /> borrows the rules <typeparamref name="TOwner" /> states. Written by
///     generated code, one step per member: <c>p.Member(m =&gt; m.Name, "Name")</c>. Not an API to write against.
/// </summary>
/// <typeparam name="TModel">The generated form model.</typeparam>
/// <typeparam name="TOwner">The aggregate or entity it is the form of.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ModelProjection<TModel, TOwner> : ModelProjection
{
    private readonly List<Action<InlineValidator<TModel>, IReadOnlyList<IValidator>, IServiceProvider?>> _steps = [];

    internal ModelProjection()
        : base(typeof(TOwner))
    {
    }

    /// <summary>A member that holds what the owner's member holds, of the same type.</summary>
    /// <typeparam name="TValue">The member's type.</typeparam>
    /// <param name="member">The model's member.</param>
    /// <param name="owner">The owner's member whose rules it carries.</param>
    /// <returns>This projection.</returns>
    public ModelProjection<TModel, TOwner> Member<TValue>(Expression<Func<TModel, TValue>> member, string owner)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentException.ThrowIfNullOrEmpty(owner);

        Members[NameOf(member)] = owner;
        _steps.Add((validator, owners, _) => BorrowedRules.Borrow<TModel, TOwner, TValue>(validator, owners, member, owner));
        return this;
    }

    /// <summary>A member that holds the owner's value as its nullable twin: a model's <c>int?</c> for an <c>int</c>.</summary>
    /// <typeparam name="TValue">The owner's member's type.</typeparam>
    /// <param name="member">The model's member.</param>
    /// <param name="owner">The owner's member whose rules it carries.</param>
    /// <returns>This projection.</returns>
    public ModelProjection<TModel, TOwner> Value<TValue>(Expression<Func<TModel, TValue?>> member, string owner)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentException.ThrowIfNullOrEmpty(owner);

        Members[NameOf(member)] = owner;
        _steps.Add((validator, owners, _) => BorrowedRules.BorrowLifted<TModel, TOwner, TValue>(validator, owners, member, owner));
        return this;
    }

    /// <summary>A list of rows of a child, each validated by the rules its own model borrows.</summary>
    /// <typeparam name="TRow">The row's form model.</typeparam>
    /// <param name="rows">The model's list.</param>
    /// <returns>This projection.</returns>
    public ModelProjection<TModel, TOwner> Rows<TRow>(Expression<Func<TModel, IEnumerable<TRow>>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        RowTypes.Add(typeof(TRow));
        _steps.Add((validator, _, services) => validator.RuleForEach(rows).SetValidator(new ChildRowsValidator<TRow>(services)));
        return this;
    }

    internal override IValidator Build(IReadOnlyList<IValidator> owner, IServiceProvider? services)
    {
        var validator = new InlineValidator<TModel>();
        foreach (var step in _steps)
        {
            step(validator, owner, services);
        }

        return validator;
    }

    private static string NameOf(LambdaExpression member) =>
        member.Body is MemberExpression named
            ? named.Member.Name
            : throw new ArgumentException("A projected member is a property of the model: m => m.Name.", nameof(member));
}
