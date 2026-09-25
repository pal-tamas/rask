namespace Rask.Generators.External.PackageIslands;

/// <summary>One prop of a package component.</summary>
/// <param name="Name">The prop's name in the package — the JSON key, unless <paramref name="Wire" /> differs.</param>
/// <param name="Wire">The key the props JSON carries (an event binding for Lit and Angular).</param>
/// <param name="Required">Whether the package requires it.</param>
/// <param name="Doc">The package's own documentation for it.</param>
/// <param name="Default">The package's documented default, as source text.</param>
/// <param name="Type">Its type.</param>
/// <param name="Line">Where it is declared in the snapshot, for diagnostics.</param>
/// <param name="Column">Where it is declared in the snapshot, for diagnostics.</param>
internal sealed record SnapshotProp(
    string Name,
    string Wire,
    bool Required,
    string? Doc,
    string? Default,
    SnapshotType Type,
    int Line,
    int Column);
