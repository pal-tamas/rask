using System.Reflection.Metadata;
using Rask.Core.ScopedCss;

[assembly: MetadataUpdateHandler(typeof(ScopedCssHotReloadHandler))]

namespace Rask.Core.ScopedCss;

internal static class ScopedCssHotReloadHandler
{
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        if (updatedTypes is null || updatedTypes.Length == 0)
        {
            ScopedCssRegistry.InvalidateAll();
            return;
        }

        foreach (var t in updatedTypes)
        {
            ScopedCssRegistry.Invalidate(t);
        }
    }
}
