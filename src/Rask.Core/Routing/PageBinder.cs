using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Routing;

internal static class PageBinder
{
    private static readonly ConcurrentDictionary<Type, BindableProperties> _propertyCache = new();

    // What each [QueryParam] property held on the page as it was built, before any address was bound into it:
    // its initializer. Kept for as long as the page is, one array for a page instance.
    private static readonly ConditionalWeakTable<Component, object?[]> _builtWith = new();

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Page concrete type's public properties are preserved via the [DynamicDependency] entries " +
                        "emitted by RoutesGenerator into __RaskRoutesRegistry.Init().")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "PropertyType flows into RouteValueParser.TryParse, which expects Interfaces|PublicMethods. " +
                        "PropertyInfo.PropertyType does not carry that DAM annotation by default, but the Roslyn " +
                        "analyser (RASK011) statically guarantees every [RouteParam]/[QueryParam] property type is " +
                        "either string or IParsable<T> — both have their TryParse paths covered by the runtime.")]
    public static bool Bind(Component page, IReadOnlyDictionary<string, string?> values, IQueryCollection query)
    {
        var bindable = _propertyCache.GetOrAdd(page.GetType(), DiscoverProperties);
        var builtWith = bindable.AnyFromQuery ? BuiltWith(page, bindable.All) : null;
        var anyChanged = false;

        for (var i = 0; i < bindable.All.Length; i++)
        {
            var property = bindable.All[i];

            // A page instance is reused across addresses, so a parameter the new address does not carry
            // must not keep the last address's value (Id=guidA carrying across /todos/{guidA}/edit → /todos
            // keeps the edit dialog open). A query parameter goes back to what the page was built with —
            // `= 1` for `?page=` — and a segment to its type's default.
            var converted = TryRead(page, property, values, query, out var read)
                ? read
                : WithoutAValue(property, builtWith, i);

            var previous = property.Property.GetValue(page);
            if (!Equals(previous, converted))
            {
                anyChanged = true;
            }

            property.Property.SetValue(page, converted);
        }

        return anyChanged;
    }

    private static object? WithoutAValue(BindablePropertyInfo property, object?[]? builtWith, int index) =>
        builtWith is not null && property.IsQueryParam
            ? builtWith[index]
            : DefaultValue(property.Property.PropertyType);

    // Read once, at the page's first bind — nothing has written to the properties yet. A reference-type
    // value is kept as the object it is: an absent parameter hands the page that same object back.
    private static object?[] BuiltWith(Component page, BindablePropertyInfo[] properties)
    {
        if (_builtWith.TryGetValue(page, out var known))
        {
            return known;
        }

        var builtWith = new object?[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            if (properties[i].IsQueryParam)
            {
                builtWith[i] = properties[i].Property.GetValue(page);
            }
        }

        _builtWith.Add(page, builtWith);
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
            foreach (var property in _propertyCache.GetOrAdd(page, DiscoverProperties).All)
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

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "Activator.CreateInstance is only invoked when t is a non-nullable value type. " +
                        "Value types always carry a public parameterless constructor, so the trimmer's " +
                        "PublicParameterlessConstructor demand is satisfied independently of the static " +
                        "annotation on this method.")]
    private static object? DefaultValue(Type t) =>
        t.IsValueType && Nullable.GetUnderlyingType(t) is null ? Activator.CreateInstance(t) : null;

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Page concrete type's public properties are preserved via the [DynamicDependency] entries " +
                        "emitted by RoutesGenerator into __RaskRoutesRegistry.Init().")]
    private static BindableProperties DiscoverProperties(Type type)
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

        return new BindableProperties(result.ToArray(), result.Exists(static p => p.IsQueryParam));
    }

    private static bool TryRead(Component page, BindablePropertyInfo property,
        IReadOnlyDictionary<string, string?> values, IQueryCollection query, out object? read)
    {
        if (TryGetSegment(property, values, out var segment))
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
        if (info.IsQueryParam
            && query.Keys.FirstOrDefault(key => string.Equals(key, info.BindingName, StringComparison.OrdinalIgnoreCase))
                is { } queryKey)
        {
            var stringValues = query[queryKey];
            text = stringValues.Count == 0 ? null : stringValues[0];
            return true;
        }

        text = null;
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

    private sealed record BindableProperties(BindablePropertyInfo[] All, bool AnyFromQuery);

    private readonly record struct BindablePropertyInfo(
        PropertyInfo Property,
        bool IsRouteParam,
        bool IsQueryParam,
        string BindingName);
}
