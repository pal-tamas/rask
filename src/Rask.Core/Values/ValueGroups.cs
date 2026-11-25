using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Rask.Values;

// The value objects of several values a form's model may hold as they are — `public Opening? Start { get; set; }`,
// `public Change<Opening?> Start { get; set; }` — each registered by its assembly's generated module initializer
// (GeneratedValueRules.Group), and bound a member at a time: `() => m.Start!.Date`.
internal static class ValueGroups
{
    private static readonly ConcurrentDictionary<Type, ValueGroup> ByValue = new();
    private static readonly ConcurrentDictionary<Type, ValueGroup> ByDraft = new();

    internal static void Add(Type draft, ValueGroup group)
    {
        ByValue[group.Value] = group;
        ByDraft[draft] = group;
    }

    // As ValueFields.Of: a type's own assembly registers it as it starts, and is run first for one named before.
    internal static ValueGroup? Of(Type type)
    {
        if (!type.IsClass || !typeof(Value).IsAssignableFrom(type))
        {
            return null;
        }

        if (ByValue.TryGetValue(type, out var group))
        {
            return group;
        }

        RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
        return ByValue.GetValueOrDefault(type);
    }

    // The value object a draft of this type is typed into; null for any other type.
    internal static ValueGroup? OfDraft(Type type) =>
        typeof(ValueDraft).IsAssignableFrom(type) ? ByDraft.GetValueOrDefault(type) : null;
}
