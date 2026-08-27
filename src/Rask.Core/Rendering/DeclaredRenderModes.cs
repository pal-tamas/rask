using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Core.Rendering;

/// <summary>
///     Reads <see cref="RenderModeAttribute" /> off a component type, once per type.
/// </summary>
/// <remarks>
///     Cached because the lookup sits on the mount path, which runs for every component of every
///     render. The attribute is a compile-time fact about a type, so the first answer is the only
///     answer — the same arrangement scoped assets use for their by-type lookup.
/// </remarks>
internal static class DeclaredRenderModes
{
    private static readonly ConcurrentDictionary<Type, RenderMode> _cache = new();

    /// <summary>The mode <paramref name="type" /> declares, or <see cref="RenderMode.Auto" />.</summary>
    // No DynamicallyAccessedMembers annotation: reading an attribute off a type needs no members
    // preserved, and requiring them here would force every caller to carry the same annotation up a
    // chain that starts at object.GetType(), which cannot satisfy it.
    internal static RenderMode Of(Type type) =>
        _cache.GetOrAdd(
            type,
            static t => t.GetCustomAttribute<RenderModeAttribute>(inherit: true)?.Mode ?? RenderMode.Auto);
}
