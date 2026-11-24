using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Rask.Core.Forms;

namespace Rask.Testing;

/// <summary>
///     The field <c>m =&gt; m.Parts[0].Quantity</c> names on a model, reached the way a form reaches it: a sub-object
///     nobody filled is made, and a row the list does not have yet is added, as <c>f.Parts.Add(new())</c> would.
/// </summary>
/// <param name="Owner">The object that holds the field — the row, not the form's root.</param>
/// <param name="Property">The field's property on <paramref name="Owner" />.</param>
/// <param name="Path">The field as a form names it: <c>Parts[0].Quantity</c>.</param>
internal sealed record FormFieldReach(object Owner, PropertyInfo Property, string Path)
{
    /// <summary>The field as the form's <see cref="EditContext" /> knows it.</summary>
    public FieldIdentifier Field => new(Owner, Property.Name);

    /// <summary>Reaches the field <paramref name="field" /> names on <paramref name="model" />.</summary>
    /// <exception cref="ArgumentException">The lambda is not a chain of properties and list indexes.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "A test helper over the test's own form models, which a test project never trims.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "A test helper over the test's own form models, which a test project never trims.")]
    public static FormFieldReach Of<TModel>(TModel model, Expression<Func<TModel, object?>> field)
        where TModel : class
    {
        var steps = Steps(field.Body, field);
        if (steps is not [.., PropertyInfo { CanWrite: true } last] || steps[0] is not PropertyInfo)
        {
            throw NotAField(field);
        }

        object owner = model;
        var path = "";
        for (var i = 0; i < steps.Count - 1; i++)
        {
            (owner, path) = steps[i] switch
            {
                PropertyInfo property => (Into(owner, property), Joined(path, property.Name)),
                int row => (Row((IList)owner, row), path + "[" + row.ToString(CultureInfo.InvariantCulture) + "]"),
                _ => throw NotAField(field),
            };
        }

        return new FormFieldReach(owner, last, Joined(path, last.Name));
    }

    /// <summary>Puts <paramref name="value" /> in the field, converted to its type the way a typed value would be.</summary>
    public void Set(object? value) => Property.SetValue(Owner, Converted(value, Property.PropertyType));

    // Root first: each step a property or a list index.
    private static List<object> Steps(Expression body, LambdaExpression field)
    {
        var steps = new List<object>();
        var at = Unboxed(body);
        while (at is not ParameterExpression)
        {
            switch (at)
            {
                case MemberExpression { Member: PropertyInfo property } member:
                    steps.Add(property);
                    at = Unboxed(member.Expression!);
                    break;
                case MethodCallExpression { Method.Name: "get_Item", Object: { } list, Arguments: [var index] }:
                    steps.Add(Index(index, field));
                    at = Unboxed(list);
                    break;
                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } array:
                    steps.Add(Index(array.Right, field));
                    at = Unboxed(array.Left);
                    break;
                default:
                    throw NotAField(field);
            }
        }

        steps.Reverse();
        return steps;
    }

    private static Expression Unboxed(Expression e) =>
        e is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert ? Unboxed(convert.Operand) : e;

    // A row's place: a literal, or a variable the lambda captured.
    private static int Index(Expression index, LambdaExpression field) =>
        index switch
        {
            ConstantExpression { Value: int row } => row,
            MemberExpression { Member: FieldInfo captured, Expression: ConstantExpression { Value: var closure } }
                when captured.GetValue(closure) is int row => row,
            _ => throw NotAField(field),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "A test helper over the test's own form models, which a test project never trims.")]
    private static object Into(object owner, PropertyInfo property)
    {
        if (property.GetValue(owner) is { } held)
        {
            return held;
        }

        var made = Activator.CreateInstance(property.PropertyType)
                   ?? throw new InvalidOperationException($"{property.Name} is empty and cannot be made with new().");
        property.SetValue(owner, made);
        return made;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "A test helper over the test's own form models, which a test project never trims.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "A test helper over the test's own form models, which a test project never trims.")]
    private static object Row(IList rows, int row)
    {
        var type = rows.GetType().GetInterfaces().Append(rows.GetType())
                       .FirstOrDefault(static t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IList<>))?
                       .GetGenericArguments()[0]
                   ?? throw new InvalidOperationException("The rows are no IList<T>, so no row can be added to reach the field.");
        while (rows.Count <= row)
        {
            rows.Add(Activator.CreateInstance(type));
        }

        return rows[row] ?? throw new InvalidOperationException($"Row {row} is null.");
    }

    private static object? Converted(object? value, Type type)
    {
        var held = Nullable.GetUnderlyingType(type) ?? type;
        return value switch
        {
            null => null,
            _ when held.IsInstanceOfType(value) => value,
            string name when held.IsEnum => Enum.Parse(held, name),
            _ when held.IsEnum => Enum.ToObject(held, value),
            IConvertible => Convert.ChangeType(value, held, CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    private static string Joined(string path, string name) => path.Length == 0 ? name : path + "." + name;

    private static ArgumentException NotAField(LambdaExpression field) =>
        new($"Test.FormShows needs a field of the model, like m => m.Name or m => m.Parts[0].Quantity; {field} is not one.",
            nameof(field));
}
