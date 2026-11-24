using System.Linq.Expressions;
using System.Reflection;

namespace Rask;

// The messages a bound field gives that name the member it is bound to.
internal static class UiBoundMember
{
    /// <summary>The field's call, as the page wrote it, for a message: <c>Ui.Field.Bind(() =&gt; model.Vehicle)</c>.</summary>
    internal static string Call(LambdaExpression bound) => "Ui.Field.Bind(() => …" + (Member(bound)?.Name is { } name ? "." + name : "") + ")";

    /// <summary>The field holds another row's id and was given no rows to pick it from.</summary>
    internal static InvalidOperationException NoChoices(LambdaExpression bound) =>
        new($"{Call(bound)} holds another row's id, and a field picks an id from rows: give .Choices(…) the rows — "
            + "the shapes Ui.Select.Choices takes, e.g. .Choices(Vehicle.Find).");

    /// <summary>A range end of a type the field has no control for.</summary>
    internal static InvalidOperationException NoControlFor(LambdaExpression bound, Type end) =>
        new($"{Call(bound)} is a range whose ends are {Spelled(end)}, which no control draws: bind each end with a control "
            + "of your own in Ui.Field[…].");

    /// <summary>A range whose shape does not name the members its ends are held by.</summary>
    internal static InvalidOperationException NoEnds(LambdaExpression bound) =>
        new($"{Call(bound)} is a range whose FieldShape names no ends: give it Start: and End:, the members that hold them.");

    // A type as C# spells it: UiInput<String>, Nullable<Int32>.
    internal static string Spelled(Type type) =>
        type.IsGenericType
            ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] + "<" + string.Join(", ", type.GetGenericArguments().Select(Spelled)) + ">"
            : type.Name;

    private static MemberInfo? Member(LambdaExpression bound)
    {
        var body = bound.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            body = convert.Operand;
        }

        return body is MemberExpression { Member: PropertyInfo or FieldInfo } member ? member.Member : null;
    }
}
