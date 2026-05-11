using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

internal static class ExpressionAccessor
{
    public static Accessor Parse(LambdaExpression expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        if (body is not MemberExpression me || me.Member is not PropertyInfo prop)
        {
            throw new ArgumentException(
                $"Bind expression must be a simple property access like '() => model.Property'. Got: {expression}",
                nameof(expression));
        }

        if (me.Expression is null)
        {
            throw new ArgumentException(
                $"Bind expression must access an instance property, not a static one. Got: {expression}",
                nameof(expression));
        }

        var targetGetter = Expression.Lambda<Func<object>>(
            Expression.Convert(me.Expression, typeof(object))).Compile();

        var target = targetGetter()
                     ?? throw new InvalidOperationException(
                         $"Bind expression target evaluated to null: {expression}");

        return new Accessor(
            target,
            prop,
            () => prop.GetValue(target),
            v => prop.SetValue(target, v));
    }

    public sealed record Accessor(
        object Target,
        PropertyInfo Property,
        Func<object?> Getter,
        Action<object?> Setter)
    {
        public Type PropertyType => Property.PropertyType;
        public string PropertyName => Property.Name;
        public FieldIdentifier Field => new(Target, Property.Name);
    }
}
