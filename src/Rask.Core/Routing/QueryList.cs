using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;

namespace Rask.Core.Routing;

/// <summary>
///     A <c>[QueryParam]</c> list: its key repeated with empty brackets, as Laravel and Rails write it —
///     <c>?drivers[]=ada&amp;drivers[]=theo</c>. Laravel's indexed <c>drivers[0]=ada&amp;drivers[1]=theo</c> reads
///     in the order of its indexes, and a plain <c>drivers=ada</c> as a list of one.
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
    ///     Every value the list <paramref name="name" /> is given, matched without regard to case: the
    ///     <c>name[]</c> pairs in order, then the <c>name[n]</c> ones by index, then any plain <c>name</c>.
    /// </summary>
    public static bool TryFind(IQueryCollection query, string name, out StringValues found)
    {
        StringValues bracketed = default;
        StringValues plain = default;
        List<(long Index, StringValues Values)>? indexed = null;
        var any = false;
        foreach (var (key, values) in query)
        {
            switch (Classify(key, name, out var index))
            {
                case KeyForm.Plain:
                    plain = StringValues.Concat(plain, values);
                    break;
                case KeyForm.Bracketed:
                    bracketed = StringValues.Concat(bracketed, values);
                    break;
                case KeyForm.Indexed:
                    (indexed ??= []).Add((index, values));
                    break;
                default:
                    continue;
            }

            any = true;
        }

        found = bracketed;
        if (indexed is not null)
        {
            foreach (var (_, values) in indexed.OrderBy(static pair => pair.Index))
            {
                found = StringValues.Concat(found, values);
            }
        }

        found = StringValues.Concat(found, plain);
        return any;
    }

    /// <summary>Whether <paramref name="key" /> carries the list <paramref name="name" />: <c>name</c>, <c>name[]</c> or <c>name[n]</c>.</summary>
    public static bool IsKeyOf(string key, string name) => Classify(key, name, out _) != KeyForm.None;

    /// <summary>The query <paramref name="query" /> with the list <paramref name="name" /> set to <paramref name="values" />, as <c>name[]</c> pairs.</summary>
    /// <remarks>Every form of the key goes first, so the list is what the address then says; no value takes the key out.</remarks>
    public static void Set(Dictionary<string, StringValues> query, string name, string[] values)
    {
        foreach (var key in query.Keys.Where(key => IsKeyOf(key, name)).ToList())
        {
            query.Remove(key);
        }

        if (values.Length > 0)
        {
            query[name + "[]"] = values;
        }
    }

    /// <summary>The elements as their URL writes them, before encoding; a null one, or one that writes as nothing, is left out.</summary>
    public static string[] Texts<T>(IEnumerable<T> values)
    {
        var texts = new List<string>();
        foreach (var value in values)
        {
            if (value is not null && RouteValueFormatter.Text(value) is { Length: > 0 } text)
            {
                texts.Add(text);
            }
        }

        return texts.ToArray();
    }

    /// <summary>
    ///     The elements, each read as a single value is; an empty or unreadable one is left out. Null when none is
    ///     left: the key reads as absent.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "elementType is the element of a [QueryParam] list; RASK011 requires it to be string or " +
                        "IParsable<T>, so RouteValueParser.TryParse's demands are met as they are for one value.")]
    public static Array? Read(StringValues values, Type elementType, out bool leftOut)
    {
        leftOut = false;
        var read = new List<object?>(values.Count);
        foreach (var text in values)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (RouteValueParser.TryParse(elementType, text, out var element))
            {
                read.Add(element);
            }
            else
            {
                leftOut = true;
            }
        }

        return read.Count == 0 ? null : ToArray(read, elementType);
    }

    private static KeyForm Classify(string key, string name, out long index)
    {
        index = 0;
        if (!key.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            return KeyForm.None;
        }

        var rest = key.AsSpan(name.Length);
        if (rest.IsEmpty)
        {
            return KeyForm.Plain;
        }

        if (rest is "[]")
        {
            return KeyForm.Bracketed;
        }

        return rest is ['[', .. var inside, ']']
               && long.TryParse(inside, NumberStyles.None, CultureInfo.InvariantCulture, out index)
            ? KeyForm.Indexed
            : KeyForm.None;
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

    private enum KeyForm
    {
        None,
        Plain,
        Bracketed,
        Indexed,
    }
}
