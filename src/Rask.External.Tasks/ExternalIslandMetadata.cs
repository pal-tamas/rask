using System;
using System.Collections.Generic;
using System.IO;
using Rask.Spa.Tasks;

namespace Rask.External.Tasks;

/// <summary>
///     What each island's C# declared, lifted back out of the compiled assembly.
/// </summary>
/// <remarks>
///     <para>
///         The build used to read an island's runtime off its file extension. That worked only while
///         every runtime owned one: React, Preact and Solid all write <c>.tsx</c>, and Angular writes
///         the same <c>.ts</c> Lit does, so an extension now names a FAMILY and the glob that found
///         the file has nothing left to decide with.
///     </para>
///     <para>
///         The generator carries the answer out as string constants and this reads them straight from
///         PE metadata — no assembly load, for the reason #650 documents: an MSBuild worker that has
///         already loaded an assembly of the same name throws, and node reuse makes that roughly one
///         build in three.
///     </para>
///     <para>
///         Shared by both tasks. One decides which ADAPTER an island is paired with, the other which
///         type-check CONFIG it belongs in, and both would be wrong in the same way from the same
///         guess — so they read one source rather than two copies of the same reasoning.
///     </para>
/// </remarks>
internal static class ExternalIslandMetadata
{
    private const string IslandNamespace = "Rask.External.Generated";
    private const string IslandTypeName = "RaskExternalIslands";

    /// <summary>
    ///     Each island's declared runtime, keyed by component name.
    /// </summary>
    /// <remarks>
    ///     Empty is the ordinary state of a project whose components have not compiled yet, or one
    ///     whose islands are all hand-declared <c>&lt;RaskExternal&gt;</c> items — not an error.
    /// </remarks>
    /// <exception cref="BadImageFormatException">The assembly could not be read.</exception>
    public static Dictionary<string, string> Runtimes(string? assemblyPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
        {
            return map;
        }

        foreach (var pair in GeneratedTypeScript.Read(assemblyPath!, IslandNamespace, IslandTypeName))
        {
            // "runtime|module". One constant rather than two fields, so it cannot go half-missing.
            var separator = pair.Value.IndexOf('|');
            map[pair.Key] = separator < 0 ? pair.Value : pair.Value.Substring(0, separator);
        }

        return map;
    }

    /// <summary>
    ///     The runtime declared for a front-end file, or null if no component claims it.
    /// </summary>
    /// <remarks>
    ///     Matched on the file name without its extension, which is the pairing rule everywhere else in
    ///     this feature: <c>Chart.cs</c> pairs with <c>Chart.tsx</c>, exactly as scoped CSS and scoped
    ///     JS already pair. RASK058 guarantees the name is unique across the project.
    /// </remarks>
    public static string? RuntimeFor(IReadOnlyDictionary<string, string> runtimes, string path) =>
        runtimes.TryGetValue(Path.GetFileNameWithoutExtension(path), out var runtime) ? runtime : null;
}
