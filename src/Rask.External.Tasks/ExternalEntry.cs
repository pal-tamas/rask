namespace Rask.External.Tasks;

/// <summary>One island, as the bundler needs to see it.</summary>
internal sealed class ExternalEntry
{
    /// <summary>The island's name — the key the browser resolves a module by.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the front-end file sitting beside the C# class.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Which adapter wraps it — a key from <see cref="ExternalRuntime.All" />.</summary>
    public string Runtime { get; set; } = ExternalRuntime.React.Key;

    /// <summary>
    ///     The package a package island imports, as its <c>Module</c> names it (<c>@mui/material</c>), or null for
    ///     an island with a front-end file.
    /// </summary>
    /// <remarks>A package island's <see cref="Source" /> is its snapshot, which nothing imports.</remarks>
    public string? Package { get; set; }

    /// <summary>
    ///     The export of <see cref="Package" /> the island mounts, as its <c>Export</c> names it — <c>default</c> when
    ///     it names none, a dotted member (<c>Switch.Root</c>), or a Lit element's tag.
    /// </summary>
    public string Export { get; set; } = "default";

    /// <summary>
    ///     The tag a Lit package island's element registers, as its snapshot records it, or null. Unused when the
    ///     <see cref="Package" /> names the tag itself.
    /// </summary>
    public string? Tag { get; set; }
}
