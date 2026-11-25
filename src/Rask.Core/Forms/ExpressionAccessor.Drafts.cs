using System.Linq.Expressions;
using System.Reflection;
using Rask.Values;

namespace Rask.Core.Forms;

// A bind to a member of a value object of several values a model holds as it is — `() => m.Start!.Date` over
// `Opening? Start`, `() => row.Start.Value!.Date` over `Change<Opening?> Start`. The value object's members are init-only
// and the property may hold none, so the control is bound to the member of its draft (ValueDrafts) instead, which makes
// the value object again as it is typed into and sets the property once its rules take the whole.
public static partial class ExpressionAccessor
{
    private static Accessor? DraftOf(MemberExpression member, LambdaExpression expression)
    {
        var holder = Unconverted(member.Expression);
        if (holder is not MemberExpression { Member: PropertyInfo held } heldBy
            || ValueGroups.Of(held.PropertyType) is not { } group)
        {
            return null;
        }

        // `row.Start.Value.Date`: the value of a change, which the property holds.
        var (property, owner) = Unconverted(heldBy.Expression) is MemberExpression { Member: PropertyInfo changed } changedBy
                                && string.Equals(held.Name, nameof(Change<object>.Value), StringComparison.Ordinal)
                                && group.IsChange(changed.PropertyType)
            ? (changed, changedBy.Expression)
            : (held, heldBy.Expression);
        if (owner is null || !group.Members.TryGetValue(member.Member.Name, out var drafted))
        {
            return null;
        }

        var target = EvaluateTarget(owner)
                     ?? throw new InvalidOperationException(
                         $"Bind({expression}) reached null before the value object it binds a member of. Create the object that holds it before the form renders.");

        return new Accessor(ValueDrafts.For(target, property, group), drafted)
        {
            Owner = FindRootConstant(member),
            UnderOptional = RaskValidation.IsUnderOptional(member.Expression),
        };
    }

    private static Expression? Unconverted(Expression? e)
    {
        while (e is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            e = u.Operand;
        }

        return e;
    }
}
