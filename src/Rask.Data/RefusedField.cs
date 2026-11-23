using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rask.Data;

// Reads the field a Refuse names out of its lambda, as a FieldFailure names it: `Km.Start`, `Parts[2].Quantity`.
// Walked from the last member back to the root, so the segments are gathered in reverse.
internal static class RefusedField
{
    // The compiler's name for a captured `this` inside a closure, and its suffix for a captured primary-constructor parameter.
    private const string CapturedThis = "<>4__this";

    internal static string PathOf(LambdaExpression field)
    {
        var segments = new List<string>();
        var node = Unwrap(field.Body);
        while (node is not null)
        {
            node = Step(node, segments, field);
        }

        if (segments.Count == 0)
        {
            throw Unreadable(field);
        }

        segments.Reverse();
        var path = new System.Text.StringBuilder();
        foreach (var segment in segments)
        {
            if (path.Length > 0 && segment[0] != '[')
            {
                path.Append('.');
            }

            path.Append(segment);
        }

        return path.ToString();
    }

    // One segment of the chain: adds it, and hands back what it was read from, or null at the root.
    private static Expression? Step(Expression node, List<string> segments, LambdaExpression field)
    {
        switch (node)
        {
            case MemberExpression { Expression: null } member:
                segments.Add(member.Member.Name);
                return null;

            case MemberExpression { Expression: ConstantExpression root } member:
                // A closure's field is a captured local or parameter, named as its form field is; anything else is
                // `this`, which the field path leaves out.
                if (IsClosure(root.Value))
                {
                    if (!string.Equals(member.Member.Name, CapturedThis, StringComparison.Ordinal))
                    {
                        segments.Add(FormName(member.Member.Name));
                    }
                }
                else
                {
                    segments.Add(MemberName(member.Member));
                }

                return null;

            case MemberExpression { Expression: { } owner } member:
                segments.Add(MemberName(member.Member));
                return Unwrap(owner);

            case ParameterExpression:
                return null;

            case BinaryExpression { NodeType: ExpressionType.ArrayIndex } element:
                segments.Add(Index([element.Right]));
                return Unwrap(element.Left);

            case MethodCallExpression { Method: { IsSpecialName: true, Name: "get_Item" }, Object: { } indexed } call:
                segments.Add(Index(call.Arguments));
                return Unwrap(indexed);

            case IndexExpression { Object: { } indexed } index:
                segments.Add(Index(index.Arguments));
                return Unwrap(indexed);

            default:
                throw Unreadable(field);
        }
    }

    // The index as the form writes it, evaluated now: `Parts[i]` is `Parts[2]`.
    private static string Index(IReadOnlyList<Expression> arguments) =>
        $"[{string.Join(",", arguments.Select(static a => Convert.ToString(Evaluate(a), CultureInfo.InvariantCulture)))}]";

    // Only ever on the way to a refusal, so interpreting the index once costs nothing an operation that goes through pays.
    // Rask.Data's server face does not reference Rask.Core, whose bind parser walks indexes without compiling.
    private static object? Evaluate(Expression index) =>
        index is ConstantExpression constant
            ? constant.Value
            : Expression.Lambda<Func<object?>>(Expression.Convert(index, typeof(object))).Compile(preferInterpretation: true)();

    // A primary-constructor parameter is kept on `this` as `<name>P`; it is named as the parameter is.
    private static string MemberName(MemberInfo member) =>
        member.Name is ['<', .. var inner, '>', 'P'] ? FormName(inner) : member.Name;

    // A local or parameter is camelCase and its form field PascalCase: `parts` is `Parts`.
    private static string FormName(string local) =>
        local.Length == 0 || char.IsUpper(local[0]) ? local : char.ToUpperInvariant(local[0]) + local[1..];

    private static bool IsClosure(object? value) =>
        value is not null && value.GetType().IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static Expression Unwrap(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } cast)
        {
            node = cast.Operand;
        }

        return node;
    }

    private static ArgumentException Unreadable(LambdaExpression field) =>
        new($"Refuse names a field as a chain of members and indexers, such as () => Parts[i].Quantity; '{field}' is not one.",
            nameof(field));
}
