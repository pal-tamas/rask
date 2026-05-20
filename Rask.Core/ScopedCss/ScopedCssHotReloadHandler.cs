using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using Rask.Core.ScopedCss;

[assembly: MetadataUpdateHandler(typeof(ScopedCssHotReloadHandler))]

namespace Rask.Core.ScopedCss;

internal static class ScopedCssHotReloadHandler
{
    private const string GeneratedRegistrationTypeName = "__RaskScopedCssRegistration";
    private const string RefreshMethodName = "RefreshAll";

    // Trimming: this handler is only invoked under `dotnet watch` (hot reload is a debug-time
    // feature; trimmed Release publishes never call MetadataUpdateHandler). The reflective
    // lookup of the generator-emitted `__RaskScopedCssRegistration` class is fine to suppress
    // because the generated class carries `[ModuleInitializer]` which the trimmer roots.
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hot reload only runs under dotnet watch; trimmed publishes never invoke it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same as IL2026.")]
    public static void UpdateApplication(Type[]? updatedTypes)
    {
        // Hot reload flow:
        //   * .cs edit on a Component → updatedTypes includes the component type, NOT the
        //     generated registration class. Registry entries stay valid; nothing to do.
        //   * .css edit → the source generator re-emits __RaskScopedCssRegistration, and
        //     UpdateApplication is called with that type. We refresh by clearing the
        //     registry and re-invoking RefreshAll() on every loaded assembly's generated
        //     class. The full clear handles deleted/renamed .css files; the re-invoke
        //     repopulates from the new IL.
        var hasGeneratedUpdate = updatedTypes is null
                                 || updatedTypes.Length == 0
                                 || updatedTypes.Any(t => t.Name == GeneratedRegistrationTypeName);

        if (!hasGeneratedUpdate)
        {
            return;
        }

        ScopedCssRegistry.InvalidateAll();

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
                // Hot reload must never throw; a single broken assembly shouldn't stop the rest.
            }
        }
    }
}
