using System.Reflection;

namespace Rask.Meta.Hosting;

/// <summary>
///     Reads what the build baked into the app assembly.
/// </summary>
/// <remarks>
///     The framework's name is decided in the <c>.csproj</c>, because the build needs it there anyway
///     — to know which output directory to publish. Baking it means <c>AddRaskMeta()</c> can take no
///     argument at all and still be certain it is fronting the framework that was actually built. The
///     same arrangement <c>Rask.Wasm.Hosting</c> uses for its bundle directory.
/// </remarks>
internal static class MetaMetadata
{
    /// <summary>The assembly-metadata key carrying <c>RaskMetaFramework</c>.</summary>
    internal const string FrameworkKey = "Rask.MetaFramework";

    /// <summary>The assembly-metadata key carrying <c>RaskMetaPublishDir</c>.</summary>
    internal const string AppDirKey = "Rask.MetaAppDir";

    /// <summary>Reads one baked value, or null when the assembly carries none.</summary>
    internal static string? Read(Assembly? assembly, string key)
    {
        if (assembly is null)
        {
            return null;
        }

        foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(attribute.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>Applies whatever the build baked, leaving anything it did not bake alone.</summary>
    internal static void Apply(MetaHostingOptions options, Assembly? assembly)
    {
        if (Read(assembly, FrameworkKey) is { } name && MetaFramework.ByName(name) is { } framework)
        {
            options.Framework = framework;
        }

        if (Read(assembly, AppDirKey) is { } appDir)
        {
            options.AppDirectory = appDir;
        }
    }
}
