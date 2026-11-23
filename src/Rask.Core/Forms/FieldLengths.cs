using System.Collections.Concurrent;

namespace Rask.Core.Forms;

// The most characters each field may hold, as model types registered them through
// RaskValidation.RegisterFieldLengths. One instance is one set of registrations and the answers given under it: a
// registration swaps the whole instance, so a control reading on every render takes no lock and never sees a half.
internal sealed class FieldLengths
{
    private readonly Dictionary<Type, Func<string, int?>> _registered;
    private readonly ConcurrentDictionary<(Type Owner, string Name), int?> _answers = new();

    private FieldLengths(Dictionary<Type, Func<string, int?>> registered) => _registered = registered;

    internal static FieldLengths None { get; } = new([]);

    internal FieldLengths With(Type modelType, Func<string, int?> maxLengthOf) =>
        new(new Dictionary<Type, Func<string, int?>>(_registered) { [modelType] = maxLengthOf });

    // The type that owns the property, then its bases: a derived model answers what its base registered.
    internal int? MaxLengthOf(Type owner, string name) =>
        _registered.Count == 0 ? null : _answers.GetOrAdd((owner, name), Ask, _registered);

    private static int? Ask((Type Owner, string Name) field, Dictionary<Type, Func<string, int?>> registered)
    {
        for (var type = field.Owner; type is not null; type = type.BaseType)
        {
            if (registered.TryGetValue(type, out var maxLengthOf) && maxLengthOf(field.Name) is { } length)
            {
                return length;
            }
        }

        return null;
    }
}
