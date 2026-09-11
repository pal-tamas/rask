using System.Reflection;

namespace Rask.Server.Prerender;

/// <summary>
///     Whether an app turned the public-page cache on with <c>&lt;RaskPrerender&gt;true&lt;/RaskPrerender&gt;</c>.
/// </summary>
/// <remarks>
///     The build records the property as assembly metadata (<c>Rask.Server.Prerender.targets</c>) rather
///     than the app saying so in <c>Program.cs</c>: prerendering is one decision an app makes about its
///     pages, and a browser-WebAssembly app makes it with the same property. Read from the entry
///     assembly, which is the app — under a test host that is the test runner, which carries no such
///     metadata, so a test gets the cache only by asking for it.
/// </remarks>
internal static class PrerenderSwitch
{
    /// <summary>The metadata key the build writes.</summary>
    internal const string MetadataKey = "Rask.Prerender";

    /// <summary>Whether <paramref name="assembly" /> carries the switch, turned on.</summary>
    internal static bool IsOn(Assembly? assembly)
    {
        if (assembly is null)
        {
            return false;
        }

        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, MetadataKey, StringComparison.Ordinal))
            {
                return string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
