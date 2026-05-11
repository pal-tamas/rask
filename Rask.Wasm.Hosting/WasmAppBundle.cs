using System.Reflection;

namespace Rask.Wasm.Hosting;

internal static class WasmAppBundle
{
    internal const string MetadataKey = "Rask.WasmAppBundleDir";

    internal static string? ResolveFromAssembly(Assembly? assembly)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var attr in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attr.Key, MetadataKey, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(attr.Value) ? null : attr.Value;
            }
        }

        return null;
    }
}
