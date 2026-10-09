using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Rask.Core.Forms;

// Names a field the way a FieldFailure does — `Name`, `Price.Amount`, `Lines[2].ValidFrom` — which is
// ModelGraphWalker.Resolve read backwards: from the object that owns the property to the path that reaches it.
internal static class FieldPath
{
    /// <summary>The path of <paramref name="field" /> from <paramref name="root" />, or null when nothing reaches its owner.</summary>
    internal static string? Of(object root, FieldIdentifier field)
    {
        if (ReferenceEquals(root, field.Model))
        {
            return field.FieldName;
        }

        return PathTo(root, field.Model) is { } owner ? $"{owner}.{field.FieldName}" : null;
    }

    // Breadth-first, so the shortest path wins when an object is reachable twice.
    private static string? PathTo(object root, object owner)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        var queue = new Queue<(object Node, string Path)>();
        queue.Enqueue((root, string.Empty));
        while (queue.Count > 0)
        {
            var (node, path) = queue.Dequeue();
            foreach (var (child, childPath) in Children(node, path))
            {
                if (ReferenceEquals(child, owner))
                {
                    return childPath;
                }

                if (visited.Add(child))
                {
                    queue.Enqueue((child, childPath));
                }
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperties on the runtime type of a model node, as ModelGraphWalker.Walk: the " +
                        "app preserves the public properties of the models its forms bind.")]
    private static IEnumerable<(object Child, string Path)> Children(object node, string path)
    {
        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || !property.CanRead || Read(property, node) is not { } value
                || ModelGraphWalker.IsLeaf(value.GetType()))
            {
                continue;
            }

            var name = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
            switch (value)
            {
                case IDictionary entries:
                    foreach (DictionaryEntry entry in entries)
                    {
                        if (Keep(entry.Value) is { } item)
                        {
                            yield return (item, string.Create(CultureInfo.InvariantCulture, $"{name}[{entry.Key}]"));
                        }
                    }

                    break;
                case IList items:
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (Keep(items[i]) is { } item)
                        {
                            yield return (item, string.Create(CultureInfo.InvariantCulture, $"{name}[{i}]"));
                        }
                    }

                    break;
                case IEnumerable:
                    break;
                default:
                    yield return (value, name);
                    break;
            }
        }
    }

    private static object? Keep(object? item) => item is null || ModelGraphWalker.IsLeaf(item.GetType()) ? null : item;

    private static object? Read(PropertyInfo property, object node)
    {
        try
        {
            return property.GetValue(node);
        }
#pragma warning disable CA1031 // A getter that throws is a property this walk cannot pass through, as in ModelGraphWalker.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
