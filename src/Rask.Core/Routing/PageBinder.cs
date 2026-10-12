using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Primitives;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Routing;

internal static class PageBinder
{
    private static readonly ConcurrentDictionary<Type, BindablePropertyInfo[]> _propertyCache = new();

    // What is remembered of a page for as long as the page is: the path it was last bound from, and what each
    // bound property held as the page was built, before any address was written into it — its initializer.
    private static readonly ConditionalWeakTable<Component, BoundPage> _bound = new();

    /// <summary>
    ///     Writes the address into the page's <c>[RouteParam]</c> and <c>[QueryParam]</c> properties, and says
    ///     whether the page now sees another address than it did: a property changed, or the
    ///     <paramref name="path" /> did — <c>/todos</c> and <c>/todos/new</c> are one page with the same
    ///     properties, and still two addresses to whatever reads <c>RouteState.Path</c>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Page concrete type's public properties are preserved via the [DynamicDependency] entries " +
                        "emitted by RoutesGenerator into __RaskRoutesRegistry.Init().")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "PropertyType flows into RouteValueParser.TryParse, which expects Interfaces|PublicMethods. " +
                        "PropertyInfo.PropertyType does not carry that DAM annotation by default, but the Roslyn " +
                        "analyser (RASK011) statically guarantees every [RouteParam]/[QueryParam] property type is " +
                        "either string or IParsable<T> — both have their TryParse paths covered by the runtime.")]
    public static bool Bind(Component page, IReadOnlyDictionary<string, string?> values, IQueryCollection query,
        string? path = null)
    {
        var properties = _propertyCache.GetOrAdd(page.GetType(), DiscoverProperties);
        if (properties.Length == 0 && path is null)
        {
            return false;
        }

        var bound = _bound.GetValue(page, static _ => new BoundPage());
        var anyChanged = bound.MovedTo(path);
        var builtWith = bound.BuiltWith ??= ReadBuiltWith(page, properties);

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];

            // A page instance is reused across addresses, and each bind starts over from the address: what
            // the new one does not carry goes back to what the page was built with — `= 1` for `?page=`,
            // `= 5` for a `{count?}` left out, and null for the `Guid? Id` of /todos/{id}/edit on the way
            // to /todos, which is what closes the edit dialog.
            var converted = TryRead(page, property, values, query, out var read) ? read : builtWith[i];

            var previous = property.Property.GetValue(page);
            if (!Equals(previous, converted))
            {
                anyChanged = true;
            }

            property.Property.SetValue(page, converted);
        }

        return anyChanged;
    }

    // Read once, at the page's first bind — nothing has written to the properties yet. A reference-type
    // value is kept as the object it is: an absent parameter hands the page that same object back.
    private static object?[] ReadBuiltWith(Component page, BindablePropertyInfo[] properties)
    {
        if (properties.Length == 0)
        {
            return [];
        }

        var builtWith = new object?[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            builtWith[i] = properties[i].Property.GetValue(page);
        }

        return builtWith;
    }

    /// <summary>
    ///     Whether every page of <paramref name="chain" /> can read the path's <paramref name="values" /> into
    ///     its <c>[RouteParam]</c> properties. A path one of them cannot read is not that route's.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "As Bind: RASK011 guarantees every [RouteParam] property type is string or IParsable<T>.")]
    public static bool CanBind(IReadOnlyList<Type> chain, Dictionary<string, string?> values)
    {
        if (values.Count == 0)
        {
            return true;
        }

        foreach (var page in chain)
        {
            foreach (var property in _propertyCache.GetOrAdd(page, DiscoverProperties))
            {
                if (TryGetSegment(property, values, out var text) && text is not null
                    && !RouteValueParser.TryParse(property.Property.PropertyType, text, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Page concrete type's public properties are preserved via the [DynamicDependency] entries " +
                        "emitted by RoutesGenerator into __RaskRoutesRegistry.Init().")]
    private static BindablePropertyInfo[] DiscoverProperties(Type type)
    {
        var result = new List<BindablePropertyInfo>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var routeParam = p.GetCustomAttribute<RouteParamAttribute>(false);
            var queryParam = p.GetCustomAttribute<QueryParamAttribute>(false);
            if (routeParam is null && queryParam is null)
            {
                continue;
            }

            result.Add(new BindablePropertyInfo(
                p,
                routeParam is not null,
                queryParam is not null,
                routeParam?.Name ?? queryParam?.Name ?? p.Name));
        }

        return result.ToArray();
    }

    private static bool TryRead(Component page, BindablePropertyInfo property,
        IReadOnlyDictionary<string, string?> values, IQueryCollection query, out object? read)
    {
        // An optional segment left out is under its name with no value: absent, like a key that is not there.
        if (TryGetSegment(property, values, out var segment) && segment is not null)
        {
            read = ConvertValue(segment, property.Property.PropertyType, property.Property.Name);
            return true;
        }

        if (TryGetQueryValue(property, query, out var text))
        {
            return ReadQueryValueOrAbsent(page, property, text, out read);
        }

        read = null;
        return false;
    }

    /// <summary>
    ///     The one rule for a query value: what its property cannot hold — <c>?weightFrom=abc</c> for an
    ///     <c>int?</c> — is read as if the parameter were not in the URL. Links with filters get mailed around
    ///     and edited by hand, so this is a visitor's input and never an exception.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "As Bind: RASK011 guarantees every [QueryParam] property type is string or IParsable<T>.")]
    private static bool ReadQueryValueOrAbsent(Component page, BindablePropertyInfo property, string? text,
        out object? read)
    {
        if (text is null)
        {
            read = null;
            return false;
        }

        if (RouteValueParser.TryParse(property.Property.PropertyType, text, out read))
        {
            return true;
        }

        if (LiveOptions.IsDevelopment == true)
        {
            SayIgnored(page.GetType(), property);
        }

        read = null;
        return false;
    }

    // Once for a page's parameter, and without the value: that is a visitor's input.
    private static void SayIgnored(Type page, BindablePropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;
        RaskDiagnostics.ReportOnce(
            $"query-ignored:{page.FullName}:{property.BindingName}",
            RaskLogLevel.Information, "Rask.Routing",
            () => $"Rask routing: {page.Name} ignored the query parameter '{property.BindingName}' — its value " +
                  $"cannot be read as {type.Name}, so {property.Property.Name} is bound as if it were absent.");
    }

    // The one way a segment is found for a property, for the match (CanBind) and the bind alike: by name,
    // whatever its case. A matched route's dictionary already compares that way and its answer is final,
    // while any other dictionary is read through.
    private static bool TryGetSegment(BindablePropertyInfo info, IReadOnlyDictionary<string, string?> values,
        out string? segment)
    {
        segment = null;
        if (!info.IsRouteParam)
        {
            return false;
        }

        if (values.TryGetValue(info.BindingName, out segment))
        {
            return true;
        }

        if (values.Count == 0 || values is Dictionary<string, string?> { Comparer: var comparer }
            && ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var (key, value) in values)
        {
            if (string.Equals(key, info.BindingName, StringComparison.OrdinalIgnoreCase))
            {
                segment = value;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetQueryValue(BindablePropertyInfo info, IQueryCollection query, out string? text)
    {
        text = null;
        if (!info.IsQueryParam || !TryFind(query, info.BindingName, out var found))
        {
            return false;
        }

        text = found.Count == 0 ? null : found[0];
        return true;
    }

    // By name, whatever its case, and without allocating on the way — this runs for every query property
    // of every bind. Rask's own collection answers from its dictionary, and only another one is walked.
    private static bool TryFind(IQueryCollection query, string name, out StringValues found)
    {
        if (query is QueryCollection known)
        {
            return known.TryGetIgnoringCase(name, out found);
        }

        if (query.TryGetValue(name, out found))
        {
            return true;
        }

        foreach (var (key, value) in query)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                found = value;
                return true;
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "targetType originates from PropertyInfo.PropertyType on a [RouteParam]/[QueryParam] " +
                        "property; RASK011 statically requires those types to be string or IParsable<T>, so " +
                        "RouteValueParser.TryParse's IL2060/IL2070 demands are met at runtime.")]
    private static object? ConvertValue(object? raw, Type targetType, string propertyName)
    {
        if (raw is null)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (raw.GetType() == underlying)
        {
            return raw;
        }

        var asString = raw as string ?? raw.ToString();
        if (asString is null)
        {
            return null;
        }

        if (RouteValueParser.TryParse(targetType, asString, out var value))
        {
            return value;
        }

        throw new RouteBindException(
            $"Failed to bind value '{asString}' to property '{propertyName}' of type '{targetType}'.");
    }

    private sealed class BoundPage
    {
        public object?[]? BuiltWith;
        private string? _path;

        // The first address a page is bound from is where it starts, not a move.
        public bool MovedTo(string? path)
        {
            if (path is null || string.Equals(_path, path, StringComparison.Ordinal))
            {
                return false;
            }

            var moved = _path is not null;
            _path = path;
            return moved;
        }
    }

    private readonly record struct BindablePropertyInfo(
        PropertyInfo Property,
        bool IsRouteParam,
        bool IsQueryParam,
        string BindingName);
}
