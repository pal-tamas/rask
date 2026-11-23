using System.Collections.Concurrent;

namespace Rask.Core.Forms;

// The shape of each field, as model types registered them through RaskValidation.RegisterFieldShapes. One instance
// is one set of registrations and the answers given under it: a registration swaps the whole instance, so a control
// reading on every render takes no lock and never sees a half.
internal sealed class FieldShapes
{
    private readonly Dictionary<Type, Func<string, FieldShape?>> _registered;
    private readonly ConcurrentDictionary<(Type Owner, string Name), FieldShape?> _answers = new();

    private FieldShapes(Dictionary<Type, Func<string, FieldShape?>> registered) => _registered = registered;

    internal static FieldShapes None { get; } = new([]);

    internal FieldShapes With(Type modelType, Func<string, FieldShape?> shapeOf) =>
        new(new Dictionary<Type, Func<string, FieldShape?>>(_registered) { [modelType] = shapeOf });

    // The type that owns the property, then its bases: a derived model answers what its base registered.
    internal FieldShape? ShapeOf(Type owner, string name) =>
        _registered.Count == 0 ? null : _answers.GetOrAdd((owner, name), Ask, _registered);

    private static FieldShape? Ask((Type Owner, string Name) field, Dictionary<Type, Func<string, FieldShape?>> registered)
    {
        for (var type = field.Owner; type is not null; type = type.BaseType)
        {
            if (registered.TryGetValue(type, out var shapeOf) && shapeOf(field.Name) is { } shape)
            {
                return shape;
            }
        }

        return null;
    }
}
