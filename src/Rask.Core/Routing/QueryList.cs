using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

/// <summary>
///     A <c>[QueryParam]</c> list: ONE key, its elements joined by commas — <c>?drivers=ada,theo</c>. An element's
///     own comma is <c>%2C</c> in the value (and a <c>%</c> is <c>%25</c>), so it reads back as one element.
/// </summary>
internal static class QueryList
{
    /// <summary>
    ///     The element type of a list a <c>[QueryParam]</c> can bind — <c>T[]</c>, <c>IReadOnlyList&lt;T&gt;</c>,
    ///     <c>IReadOnlyCollection&lt;T&gt;</c> or <c>IEnumerable&lt;T&gt;</c> — or null for any other type.
    /// </summary>
    public static Type? ElementOf(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        Type? element = null;
        if (type.IsArray)
        {
            element = type.GetArrayRank() == 1 ? type.GetElementType() : null;
        }
        else if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)
                                                       || definition == typeof(IEnumerable<>))
            {
                element = type.GetGenericArguments()[0];
            }
        }

        // An element that may be null has nothing to say an unreadable one does not: it is left out.
        return element is null || Nullable.GetUnderlyingType(element) is not null ? null : element;
    }

    /// <summary>
    ///     The elements of every value of the key, each read as a single value is; an empty or unreadable one is
    ///     left out. Null when none is left: the key reads as absent.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "elementType is the element of a [QueryParam] list; RASK011 requires it to be string or " +
                        "IParsable<T>, so RouteValueParser.TryParse's demands are met as they are for one value.")]
    public static Array? Read(StringValues values, Type elementType, out bool leftOut)
    {
        leftOut = false;
        var most = 0;
        foreach (var text in values)
        {
            most += text is null ? 0 : text.AsSpan().Count(',') + 1;
        }

        Array? items = null;
        var count = 0;
        foreach (var text in values)
        {
            var rest = text.AsSpan();
            while (!rest.IsEmpty)
            {
                var comma = rest.IndexOf(',');
                var piece = comma < 0 ? rest : rest[..comma];
                rest = comma < 0 ? [] : rest[(comma + 1)..];
                if (piece.IsEmpty)
                {
                    continue;
                }

                if (!RouteValueParser.TryParse(elementType, Element(piece, text!), out var read)
                    || (items ??= NewArray(elementType, most)) is null)
                {
                    leftOut = true;
                    continue;
                }

                items.SetValue(read, count++);
            }
        }

        return count == 0 || items is null ? null : Trimmed(items, elementType, count);
    }

    /// <summary>Whether two lists hold equal elements in the same order.</summary>
    public static bool SameElements(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is not IEnumerable left || b is not IEnumerable right
            || (a is ICollection l && b is ICollection r && l.Count != r.Count))
        {
            return false;
        }

        var x = left.GetEnumerator();
        var y = right.GetEnumerator();
        try
        {
            while (true)
            {
                var more = x.MoveNext();
                if (more != y.MoveNext())
                {
                    return false;
                }

                if (!more)
                {
                    return true;
                }

                if (!Equals(x.Current, y.Current))
                {
                    return false;
                }
            }
        }
        finally
        {
            (x as IDisposable)?.Dispose();
            (y as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    ///     The list as one query value, before the URL encodes it: <c>ada,Lovelace%2C Ada</c>. Null when there is
    ///     no element to write — a null, or one that writes as nothing, is left out.
    /// </summary>
    public static string? Value<T>(IEnumerable<T>? values)
    {
        if (values is null)
        {
            return null;
        }

        string? first = null;
        StringBuilder? joined = null;
        foreach (var value in values)
        {
            if (value is null || RouteValueFormatter.Text(value) is not { Length: > 0 } text)
            {
                continue;
            }

            var element = text.AsSpan().IndexOfAny('%', ',') < 0
                ? text
                : text.Replace("%", "%25", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);
            if (first is null)
            {
                first = element;
                continue;
            }

            joined ??= new StringBuilder(first);
            joined.Append(',').Append(element);
        }

        return joined?.ToString() ?? first;
    }

    // The whole value is one element far more often than not, and is then that string itself.
    private static string Element(ReadOnlySpan<char> piece, string text)
    {
        if (piece.Contains('%'))
        {
            return Uri.UnescapeDataString(piece);
        }

        return piece.Length == text.Length ? text : piece.ToString();
    }

    private static Array Trimmed(Array items, Type elementType, int count)
    {
        if (count == items.Length)
        {
            return items;
        }

        var exact = NewArray(elementType, count)!;
        Array.Copy(items, exact, count);
        return exact;
    }

    // From the registry, which has every parsable type the build saw; a type it did not see is made the
    // dynamic way where the runtime can, and is otherwise unreadable.
    private static Array? NewArray(Type elementType, int length)
    {
        if (TypedParserRegistry.TryGetArray(elementType, out var array))
        {
            return array(length);
        }

        return RuntimeFeature.IsDynamicCodeSupported ? Array.CreateInstance(elementType, length) : null;
    }
}
