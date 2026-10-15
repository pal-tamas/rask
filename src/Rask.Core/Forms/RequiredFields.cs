using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Core.Forms;

// Which fields must be filled, as one answer: what a model type registered through
// RaskValidation.RegisterRequiredFields, or a [Required] on the member itself.
//
// One instance is one set of registrations and the answers given under it. A registration swaps the whole
// instance, so an answer remembered here can never outlive the registrations it was worked out from, and a
// reader that took the instance before the swap finishes on a consistent pair.
internal sealed class RequiredFields
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

    private readonly Dictionary<Type, Func<string, bool>> _registered;
    private readonly Dictionary<Type, Func<string, bool>> _optional;
    private readonly ConcurrentDictionary<(Type Owner, string Name), bool> _answers;

    private RequiredFields(
        Dictionary<Type, Func<string, bool>> registered,
        Dictionary<Type, Func<string, bool>> optional,
        ConcurrentDictionary<(Type Owner, string Name), bool> answers)
    {
        _registered = registered;
        _optional = optional;
        _answers = answers;
    }

    internal static RequiredFields None { get; } = new([], [], new());

    internal RequiredFields With(Type modelType, Func<string, bool> isRequired) =>
        new(new Dictionary<Type, Func<string, bool>>(_registered) { [modelType] = isRequired }, _optional, new());

    // The answers kept are about a property on its own type, which this does not change: they are carried over.
    internal RequiredFields WithOptional(Type modelType, Func<string, bool> isOptional) =>
        new(_registered, new Dictionary<Type, Func<string, bool>>(_optional) { [modelType] = isOptional }, _answers);

    // Whether the property holds an object that may be left wholly empty, so nothing under it has to be filled.
    // The type that declares the property speaks for every type derived from it.
    internal bool IsOptional(Type owner, string name)
    {
        if (_optional.Count == 0)
        {
            return false;
        }

        for (var type = owner; type is not null; type = type.BaseType)
        {
            if (_optional.TryGetValue(type, out var isOptional) && isOptional(name))
            {
                return true;
            }
        }

        return false;
    }

    // The same, asked of a bind expression: whether anything it walks through on the way to its last property
    // is such a property. One count read for the app that registered none, which is most renders.
    internal bool IsUnderOptional(Expression? holder)
    {
        if (_optional.Count == 0)
        {
            return false;
        }

        for (var at = holder; at is not null;)
        {
            switch (at)
            {
                case MemberExpression { Member: PropertyInfo { DeclaringType: { } owner } slot } member:
                    if (IsOptional(owner, slot.Name))
                    {
                        return true;
                    }

                    at = member.Expression;
                    break;
                case MemberExpression member:
                    at = member.Expression;
                    break;
                case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } lifted:
                    at = lifted.Operand;
                    break;
                case MethodCallExpression { Method: { IsSpecialName: true, Name: "get_Item" }, Object: { } indexed }:
                    at = indexed;
                    break;
                case IndexExpression { Object: { } indexed }:
                    at = indexed;
                    break;
                case BinaryExpression { NodeType: ExpressionType.ArrayIndex } element:
                    at = element.Left;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    // A plain property name is the whole of a bound control's question, on every render: one lookup, nothing built.
    internal bool IsRequired(Type modelType, string fieldPath)
    {
        if (fieldPath.AsSpan().IndexOfAny('.', '[') < 0)
        {
            return Answer(modelType, fieldPath);
        }

        return Locate(modelType, fieldPath) is var (owner, name) && Answer(owner, name);
    }

    private bool Answer(Type owner, string name) =>
        _answers.GetOrAdd((owner, name), static (field, fields) => fields.Ask(field.Owner, field.Name), this);

    private bool Ask(Type owner, string name) => Registered(owner, name) || Attributed(owner, name);

    // The type that declares the property speaks for every type derived from it.
    private bool Registered(Type owner, string name)
    {
        for (var type = owner; type is not null; type = type.BaseType)
        {
            if (_registered.TryGetValue(type, out var isRequired) && isRequired(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Attributed(Type owner, string name) =>
        Property(owner, name) is { } property && Attribute.IsDefined(property, typeof(RequiredAttribute), inherit: true);

    // `Rows[2].Weight` is `Weight` on the type of a row and `Price.Amount` is `Amount` on the type of `Price`,
    // read from the declared types: there is no instance here to ask, and the index says nothing about the type.
    private (Type Owner, string Name)? Locate(Type modelType, string fieldPath)
    {
        var segments = ModelGraphWalker.SplitPath(fieldPath);
        if (segments.Count == 0 || segments[^1].IsIndexed)
        {
            return null;
        }

        var owner = modelType;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            // Nothing under an object that may be left wholly empty has to be filled.
            if (IsOptional(owner, segments[i].Name))
            {
                return null;
            }

            var reached = Property(owner, segments[i].Name)?.PropertyType;
            if (reached is not null && segments[i].IsIndexed)
            {
                reached = ItemType(reached);
            }

            if (reached is null)
            {
                return null;
            }

            owner = Nullable.GetUnderlyingType(reached) ?? reached;
        }

        return (owner, segments[^1].Name);
    }

    // The nearest declaration wins, so a property that hides one of its base type's answers for itself.
    // Level by level rather than one GetProperty, which throws on a name declared twice along the way.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Looks one public property up by name on a model type a form binds, as " +
                        "ModelGraphWalker.Resolve: the app preserves the public properties of the models its " +
                        "forms bind, and a property that was trimmed away is one nothing binds, which answers false.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The same lookup on the model type's base types — see IL2070.")]
    private static PropertyInfo? Property(Type owner, string name)
    {
        for (var type = owner; type is not null; type = type.BaseType)
        {
            foreach (var declared in type.GetProperties(Declared))
            {
                if (string.Equals(declared.Name, name, StringComparison.Ordinal) && declared.GetIndexParameters().Length == 0)
                {
                    return declared;
                }
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reads which collection interfaces a model's property type implements — see Property.")]
    private static Type? ItemType(Type collection)
    {
        if (collection.IsArray)
        {
            return collection.GetElementType();
        }

        return Closed(collection, typeof(IDictionary<,>))?.GetGenericArguments()[1]
               ?? Closed(collection, typeof(IReadOnlyDictionary<,>))?.GetGenericArguments()[1]
               ?? Closed(collection, typeof(IEnumerable<>))?.GetGenericArguments()[0];

        static Type? Closed(Type type, Type open) =>
            Is(type, open) ? type : Array.Find(type.GetInterfaces(), candidate => Is(candidate, open));

        static bool Is(Type type, Type open) => type.IsGenericType && type.GetGenericTypeDefinition() == open;
    }
}
