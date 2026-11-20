using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core.Forms;

// Whether a property of an entity is an Entity Framework navigation that is not loaded — which validation must not read,
// because reading it is what loads it: one query per navigation per validation, through a lazy-loading proxy or an
// entity that was handed an ILazyLoader.
//
// Core takes no dependency on Entity Framework, so this is by shape, not by type: an entity that can lazy-load holds an
// ILazyLoader (Microsoft.EntityFrameworkCore.Infrastructure) — a proxy in its LazyLoader property, an entity of the
// "injected service" pattern in a field or property of its own — and EF's loader answers IsLoaded(entity, navigation).
// Both are found by name, once per type, and asked before the walk reads a property that is not a leaf. A loader that
// cannot answer (none, a context disposed) is taken as "not loaded": validation never loads anything. The other
// lazy-loading pattern EF offers — an Action<object, string> delegate — cannot be asked, and is not detected; mark such
// a navigation [SkipValidation].
internal static class UnloadedNavigation
{
    private const string LoaderTypeName = "Microsoft.EntityFrameworkCore.Infrastructure.ILazyLoader";

    private static readonly ConcurrentDictionary<Type, Func<object, object?>?> Loaders = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> IsLoadedOf = new();

    /// <summary>Whether <paramref name="instance" />'s <paramref name="property" /> is a navigation EF has not loaded.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reads the runtime type of the user's entity and of EF's loader by name; when trimmed away there is " +
                        "no loader to find, and the property is walked as before. See ModelGraphWalker.")]
    internal static bool Is(object instance, PropertyInfo property)
    {
        if (Loaders.GetOrAdd(instance.GetType(), FindLoader) is not { } read || read(instance) is not { } loader)
        {
            return false;
        }

        var isLoaded = IsLoadedOf.GetOrAdd(loader.GetType(), FindIsLoaded);
        if (isLoaded is null)
        {
            return true;
        }

        try
        {
            return isLoaded.Invoke(loader, [instance, property.Name]) is not true;
        }
        catch (TargetInvocationException)
        {
            return true;
        }
    }

    /// <summary>Whether <paramref name="type" /> is EF's lazy loader, which the walk never goes into (it leads to the context).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "The interfaces of a value the walk reached; a trimmed-away one is no loader to recognise.")]
    internal static bool IsLoader(Type type) =>
        string.Equals(type.FullName, LoaderTypeName, StringComparison.Ordinal)
        || type.GetInterfaces().Any(static i => string.Equals(i.FullName, LoaderTypeName, StringComparison.Ordinal));

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "EF's loader's public IsLoaded, found on its runtime type; trimmed away, the navigation is skipped.")]
    private static MethodInfo? FindIsLoaded(Type loader) =>
        loader.GetMethod("IsLoaded", BindingFlags.Instance | BindingFlags.Public, [typeof(object), typeof(string)]);

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Fields and properties of the user's entity type, found by the loader's type name. See Is.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The same, on the entity's base types (a proxy derives from the entity).")]
    private static Func<object, object?>? FindLoader(Type type)
    {
        // Non-public too: the "injected service" pattern keeps its loader in a private field or property. Only read,
        // never written, and only a member whose type is EF's loader.
#pragma warning disable S3011 // reading the entity's own private ILazyLoader is the point, see above
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
#pragma warning restore S3011
        for (var at = type; at is not null && at != typeof(object); at = at.BaseType)
        {
            if (at.GetProperties(all).FirstOrDefault(static p => p.GetIndexParameters().Length == 0 && p.CanRead && IsLoaderType(p.PropertyType)) is { } property)
            {
                return property.GetValue;
            }

            if (at.GetFields(all).FirstOrDefault(static f => IsLoaderType(f.FieldType)) is { } field)
            {
                return field.GetValue;
            }
        }

        return null;
    }

    private static bool IsLoaderType(Type type) => string.Equals(type.FullName, LoaderTypeName, StringComparison.Ordinal);
}
