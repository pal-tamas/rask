using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace Rask.Spa.Hosting;

/// <summary>
///     Finds the built app, and tells a bundler's output from a Rask WebAssembly bundle.
/// </summary>
/// <remarks>
///     The build targets bake an <see cref="AssemblyMetadataAttribute" /> naming the dist directory. That
///     path is absolute and belongs to the machine that built it, so it is deliberately <em>not</em> the
///     first thing consulted: inside a <c>rask deploy</c> container it points at a directory that does not
///     exist. The publish target copies the bundle next to the app, and that copy wins.
/// </remarks>
internal static class SpaAppBundle
{
    /// <summary>The dist directory, baked at build time.</summary>
    internal const string DistMetadataKey = "Rask.SpaDistDir";

    /// <summary>The client project directory, baked so tooling can find the sources.</summary>
    internal const string ClientMetadataKey = "Rask.SpaClientDir";

    /// <summary>Where the bundler's dev server listens, baked from the client's configuration.</summary>
    internal const string DevServerMetadataKey = "Rask.SpaDevServerUrl";

    /// <summary>The Rask WebAssembly client project this host serves, baked when there is one.</summary>
    internal const string WasmClientMetadataKey = "Rask.SpaWasmClient";

    /// <summary>
    ///     The WebAssembly client's build-output static-web-assets manifest. Baked only when the build
    ///     skipped the client's publish (<c>RaskSpaBuild=false</c>, which <c>rask dev</c> passes), so the
    ///     build's decision and the runtime's cannot drift apart.
    /// </summary>
    internal const string DevManifestMetadataKey = "Rask.SpaDevManifest";

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
                && !string.IsNullOrWhiteSpace(attribute.Value))
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

    /// <summary>
    ///     Whether a built app is a Rask WebAssembly bundle rather than a bundler's output: Rask's boot
    ///     module at the root, and the .NET runtime's loader under <c>_framework/</c>.
    /// </summary>
    /// <remarks>
    ///     Decided by looking at the files rather than by a build flag, so a bundle published anywhere and
    ///     handed over as <c>distPath</c> is served correctly. The loader is matched by prefix because the
    ///     SDK fingerprints it (<c>dotnet.7a8b9c2d3e.js</c>) when asked to.
    /// </remarks>
    internal static bool IsRaskWasm(IFileProvider files)
    {
        if (!files.GetFileInfo("rask.wasm.js").Exists)
        {
            return false;
        }

        foreach (var file in files.GetDirectoryContents("_framework"))
        {
            if (file.Name.StartsWith("dotnet", StringComparison.Ordinal)
                && file.Name.EndsWith(".js", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
