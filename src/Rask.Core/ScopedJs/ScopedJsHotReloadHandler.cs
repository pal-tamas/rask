using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using Rask.Core.ScopedAssets;
using Rask.Core.ScopedJs;

// As with ScopedCssHotReloadHandler: the type name + folder stays as the stable
// MetadataUpdateHandler anchor; the legacy ScopedJsRegistry it used to feed is gone.

[assembly: MetadataUpdateHandler(typeof(ScopedJsHotReloadHandler))]

namespace Rask.Core.ScopedJs;

internal static class ScopedJsHotReloadHandler
{
    private const string GeneratedRegistrationTypeName = "__RaskScopedJsRegistration";
    private const string RefreshMethodName = "RefreshAll";

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hot reload only runs under dotnet watch; trimmed publishes never invoke it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same as IL2026.")]
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        var hasGeneratedUpdate = updatedTypes is null
                                 || updatedTypes.Length == 0
                                 || updatedTypes.Any(t => t.Name == GeneratedRegistrationTypeName);

        if (!hasGeneratedUpdate)
        {
            return;
        }

        // Per-kind invalidate — clears JS bucket of the registry; CSS state untouched.
        ScopedAssetRegistry.InvalidateAllJs();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(GeneratedRegistrationTypeName, false);
                if (t is null)
                {
                    continue;
                }

                var method = t.GetMethod(RefreshMethodName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                method?.Invoke(null, null);
            }
            catch
            {
                // Hot reload must never throw.
            }
        }
    }
}
