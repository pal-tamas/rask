using System.Reflection;

namespace Rask.Wasm.Hosting;

internal static class WasmAppBundle
{
    internal const string MetadataKey = "Rask.WasmAppBundleDir";

    /// <summary>
    ///     The client's build-output static-web-assets manifest, baked by Rask.Wasm.Hosting.targets.
    ///     Present whenever a WASM ProjectReference was found; whether it is <i>used</i> is decided at
    ///     runtime by <c>UseRask</c> (Development + hot reload supported + the file actually exists).
    /// </summary>
    internal const string DevManifestKey = "Rask.WasmDevManifest";

    internal static string? ResolveFromAssembly(Assembly? assembly) => Read(assembly, MetadataKey);

    internal static string? ResolveDevManifest(Assembly? assembly) => Read(assembly, DevManifestKey);

    private static string? Read(Assembly? assembly, string key)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, key, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
            }
        }

        return null;
    }
}
