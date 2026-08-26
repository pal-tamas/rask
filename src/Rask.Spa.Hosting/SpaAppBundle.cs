using System.Reflection;

namespace Rask.Spa.Hosting;

/// <summary>
///     Finds the bundler's build output.
/// </summary>
/// <remarks>
///     The build targets bake an <see cref="AssemblyMetadataAttribute" /> naming the dist directory, the
///     way <c>Rask.Wasm.Hosting</c> bakes <c>Rask.WasmAppBundleDir</c>. That path is absolute and belongs
///     to the machine that built it, so it is deliberately <em>not</em> the first thing consulted: inside
///     a <c>rask deploy</c> container it points at a directory that does not exist. The publish target
///     copies the bundle next to the app, and that copy wins.
/// </remarks>
internal static class SpaAppBundle
{
    /// <summary>The dist directory, baked at build time.</summary>
    internal const string DistMetadataKey = "Rask.SpaDistDir";

    /// <summary>The client project directory, baked so tooling can find the sources.</summary>
    internal const string ClientMetadataKey = "Rask.SpaClientDir";

    /// <summary>Where the bundler's dev server listens, baked from the client's configuration.</summary>
    internal const string DevServerMetadataKey = "Rask.SpaDevServerUrl";

    /// <summary>Reads one baked metadata value, or null when the assembly carries none.</summary>
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

    /// <summary>
    ///     Resolves the directory holding the built app, or null when there is none to serve.
    /// </summary>
    /// <remarks>
    ///     In order: an explicit path, then the content root's <c>wwwroot</c> if it actually holds the
    ///     index document, then the baked build path. The middle step is the deployed case and has to
    ///     come before the baked one, or a published container would chase a build-machine path.
    ///     Requiring the index document rather than just the directory matters because an ASP.NET
    ///     project template creates an empty <c>wwwroot</c> whether or not anything fills it.
    /// </remarks>
    internal static string? Resolve(
        string? explicitPath,
        string? contentRootPath,
        Assembly? entryAssembly,
        string indexFileName)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return Directory.Exists(explicitPath) ? explicitPath : null;
        }

        if (!string.IsNullOrEmpty(contentRootPath))
        {
            var published = Path.Combine(contentRootPath, "wwwroot");
            if (File.Exists(Path.Combine(published, indexFileName)))
            {
                return published;
            }
        }

        var baked = Read(entryAssembly, DistMetadataKey);
        return !string.IsNullOrEmpty(baked) && Directory.Exists(baked) ? baked : null;
    }
}
