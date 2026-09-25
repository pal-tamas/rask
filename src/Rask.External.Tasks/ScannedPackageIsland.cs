using System.IO;

namespace Rask.External.Tasks;

/// <summary>One package island found in the source, before the compile.</summary>
/// <remarks>A class rather than a record: this assembly targets netstandard2.0, which has no init accessors.</remarks>
internal sealed class ScannedPackageIsland
{
    /// <summary>Records one package island as the scan found it.</summary>
    /// <param name="name">The class's simple name.</param>
    /// <param name="runtime">The runtime its base chain reaches.</param>
    /// <param name="module">The constant its <c>Module</c> override returns, or empty when it declares none.</param>
    /// <param name="export">The constant its <c>Export</c> override returns, or null when it declares none.</param>
    /// <param name="declaringFile">The file holding the override — the snapshot is written beside it.</param>
    /// <param name="line">The 1-based line of the override, for diagnostics.</param>
    /// <param name="fromDeclaration">Whether a package declaration (<c>Mui : ReactPackage</c>) exported it.</param>
    public ScannedPackageIsland(string name, string runtime, string module, string? export, string declaringFile,
        int line, bool fromDeclaration = false)
    {
        FromDeclaration = fromDeclaration;
        Name = name;
        Runtime = runtime;
        Module = module;
        Export = export;
        DeclaringFile = declaringFile;
        Line = line;
    }

    /// <summary>The class's simple name.</summary>
    public string Name { get; }

    /// <summary>Whether a package declaration exported it, rather than a class of its own naming a Module.</summary>
    public bool FromDeclaration { get; }

    /// <summary>The runtime its base chain reaches.</summary>
    public string Runtime { get; }

    /// <summary>The constant its <c>Module</c> override returns.</summary>
    public string Module { get; }

    /// <summary>The constant its <c>Export</c> override returns, or null for the package's default export.</summary>
    public string? Export { get; }

    /// <summary>Whether <see cref="Module" /> names a package; when it does not, the class only declared an Export.</summary>
    public bool IsPackage => ExternalPackageSpecifier.IsBare(Module);

    /// <summary>The export the island mounts: its <see cref="Export" />, or <c>default</c>.</summary>
    public string ExportOrDefault => Export ?? "default";

    /// <summary>The file holding the override.</summary>
    public string DeclaringFile { get; }

    /// <summary>The 1-based line of the override.</summary>
    public int Line { get; }

    /// <summary>Where this island's props snapshot lives.</summary>
    public string SnapshotPath =>
        Path.Combine(Path.GetDirectoryName(DeclaringFile) ?? string.Empty, Name + ".props.json");
}
