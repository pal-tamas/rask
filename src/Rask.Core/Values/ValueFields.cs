using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Rask.Values;

// The value objects of one value a form's model may hold as they are — `public TankCapacity? Size { get; set; }` —
// each registered by its assembly's generated module initializer (GeneratedValueRules.Field): the value a control
// shows and is typed into, how the value object is made from it, and what its field is.
internal static class ValueFields
{
    private static readonly ConcurrentDictionary<Type, ValueField> Registered = new();

    internal static void Add(Type type, ValueField field) => Registered[type] = field;

    // A type's own assembly registers it as it starts; a type named before anything of that assembly ran — a form's
    // model in an assembly of its own — has it run first.
    internal static ValueField? Of(Type type)
    {
        if (!type.IsClass || !typeof(Value).IsAssignableFrom(type))
        {
            return null;
        }

        if (Registered.TryGetValue(type, out var field))
        {
            return field;
        }

        RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
        return Registered.GetValueOrDefault(type);
    }

    // The type a control over this type holds: a value object's own value, else the type itself.
    internal static Type HeldOr(Type type) => Of(type)?.Held ?? type;
}
