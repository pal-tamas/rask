using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core.Forms;

// Reflective traversal helpers shared by the opt-in validator packages so a single
// `DataAnnotationsValidator()` / `FluentValidationValidator(...)` at the top of a form
// covers nested sub-objects and collection items without per-level opt-in.
//
// Trim safety: the model graph is reflected over at runtime. Consumers must preserve the
// public properties of every reachable model type — typically through
// `[DynamicallyAccessedMembers]` on the binding source or a TrimmerRoot descriptor. The
// guidance for the root model in Rask.Validation.DataAnnotations applies to every nested
// type too.
public static class ModelGraphWalker
{
    // Breadth-first walk over `root` and everything reachable through its public instance
    // properties / IEnumerable items / IDictionary values. Yields each visited reference at
    // most once (cycle-safe via reference equality). Leaves — primitives, strings, enums,
    // decimals, DateTime/DateOnly/TimeOnly/TimeSpan, Guid, Uri, and their nullable variants —
    // are not yielded and do not propagate the walk; collections themselves *are* yielded,
    // but TryValidateObject on a List<T> is a no-op so this is harmless.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Walks the user's model graph via reflection. The consumer is responsible for " +
                        "preserving the public properties of every reachable model type — same constraint " +
                        "as the root-model rule documented in Rask.Validation.DataAnnotations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperties on each visited object's runtime type — see IL2026 justification.")]
    public static IEnumerable<object> Walk(object root)
    {
        if (root is null)
        {
            yield break;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<object>();
        queue.Enqueue(root);
        visited.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            foreach (var child in EnumerateChildren(current))
            {
                if (child is null)
                {
                    continue;
                }

                if (IsLeaf(child.GetType()))
                {
                    continue;
                }

                if (!visited.Add(child))
                {
                    continue;
                }

                queue.Enqueue(child);
            }
        }
    }

    // Resolves a FluentValidation-style property path (e.g. "Address.Street", "Items[0].Name",
    // "Settings[\"smtp\"].Host") against `root`. Returns (owner, terminalPropertyName) when the
    // path lands on a property access. Returns null when:
    //   - the path is empty / null,
    //   - the terminal segment is an indexer with no trailing property (e.g. "Items[0]"),
    //   - any intermediate step can't be followed (missing property, out-of-range, null hop).
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflects over the user's model graph — see Walk for the trim-safety contract.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the resolved owner's runtime type — see Walk.")]
    public static (object Owner, string Property)? Resolve(object root, string path)
    {
        if (root is null || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var segments = SplitPath(path);
        if (segments.Count == 0)
        {
            return null;
        }

        var current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current is null)
            {
                return null;
            }

            current = StepInto(current, segments[i]);
        }

        var last = segments[^1];
        if (last.IsIndexed || current is null)
        {
            return null;
        }

        var prop = current.GetType().GetProperty(last.Name,
            BindingFlags.Instance | BindingFlags.Public);
        return prop is null ? null : (current, last.Name);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperties on the runtime type of the instance — see Walk.")]
    private static IEnumerable<object?> EnumerateChildren(object instance)
    {
        // Dictionaries: walk values, skip keys (typically primitives; validators rarely
        // target dictionary keys directly).
        if (instance is IDictionary dict)
        {
            foreach (var v in dict.Values)
            {
                yield return v;
            }

            yield break;
        }

        // Plain collections: yield items. Strings implement IEnumerable but we treat them
        // as leaves (IsLeaf handles it before enqueue, but check here too so we don't
        // enumerate their characters).
        if (instance is IEnumerable seq and not string)
        {
            foreach (var item in seq)
            {
                yield return item;
            }

            yield break;
        }

        var type = instance.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!prop.CanRead)
            {
                continue;
            }

            object? value;
            try
            {
                value = prop.GetValue(instance);
            }
            catch
            {
                continue;
            }

            yield return value;
        }
    }

    internal static bool IsLeaf(Type type)
    {
        if (type.IsPrimitive)
        {
            return true;
        }

        if (type.IsEnum)
        {
            return true;
        }

        if (type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Uri))
        {
            return true;
        }

        var underlying = Nullable.GetUnderlyingType(type);
        return underlying is not null && IsLeaf(underlying);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "GetProperty on the runtime type of the current node — see Walk.")]
    private static object? StepInto(object current, PathSegment seg)
    {
        var prop = current.GetType().GetProperty(seg.Name,
            BindingFlags.Instance | BindingFlags.Public);
        if (prop is null)
        {
            return null;
        }

        object? value;
        try
        {
            value = prop.GetValue(current);
        }
        catch
        {
            return null;
        }

        if (!seg.IsIndexed || value is null)
        {
            return value;
        }

        var key = seg.Index!;
        if (value is Array arr)
        {
            return int.TryParse(key, out var idx) && idx >= 0 && idx < arr.Length
                ? arr.GetValue(idx)
                : null;
        }

        if (value is IList list)
        {
            return int.TryParse(key, out var idx) && idx >= 0 && idx < list.Count
                ? list[idx]
                : null;
        }

        if (value is IDictionary dict)
        {
            if (dict.Contains(key))
            {
                return dict[key];
            }

            return int.TryParse(key, out var i) && dict.Contains(i) ? dict[i] : null;
        }

        return null;
    }

    private static List<PathSegment> SplitPath(string path)
    {
        var segments = new List<PathSegment>();
        var i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < path.Length && path[i] != '.' && path[i] != '[')
            {
                i++;
            }

            var name = path.Substring(start, i - start);
            if (name.Length == 0)
            {
                return new List<PathSegment>();
            }

            string? index = null;
            if (i < path.Length && path[i] == '[')
            {
                var close = path.IndexOf(']', i + 1);
                if (close < 0)
                {
                    return new List<PathSegment>();
                }

                index = path.Substring(i + 1, close - i - 1);
                if (index.Length >= 2 && index[0] == '"' && index[^1] == '"')
                {
                    index = index.Substring(1, index.Length - 2);
                }

                i = close + 1;
            }

            segments.Add(new PathSegment(name, index));
        }

        return segments;
    }

    private readonly record struct PathSegment(string Name, string? Index)
    {
        public bool IsIndexed => Index is not null;
    }
}
