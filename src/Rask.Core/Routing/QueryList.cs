using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

/// <summary>
///     A <c>[QueryParam]</c> list: ONE key, its elements joined by commas — <c>?drivers=ada,theo</c>, or
///     <c>?drivers=ada%2Ctheo</c> as a URL writes it. The value is split on its decoded commas.
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
        var read = new List<object?>();
        foreach (var text in values)
        {
            if (text is null)
            {
                continue;
            }

            foreach (var range in text.AsSpan().Split(','))
            {
                var (start, length) = range.GetOffsetAndLength(text.Length);
                if (length == 0)
                {
                    continue;
                }

                // The whole value is one element far more often than not, and is then that string itself.
                var piece = length == text.Length ? text : text.Substring(start, length);
                if (RouteValueParser.TryParse(elementType, piece, out var element))
                {
                    read.Add(element);
                }
                else
                {
                    leftOut = true;
                }
            }
        }

        return read.Count == 0 ? null : ToArray(read, elementType);
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

    // A T[] — what every list type a [QueryParam] can bind is assigned from.
    private static Array? ToArray(List<object?> read, Type elementType)
    {
        var array = NewArray(elementType, read.Count);
        if (array is null)
        {
            return null;
        }

        for (var i = 0; i < read.Count; i++)
        {
            array.SetValue(read[i], i);
        }

        return array;
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
