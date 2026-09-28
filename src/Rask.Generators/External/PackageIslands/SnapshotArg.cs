namespace Rask.Generators.External.PackageIslands;

/// <summary>A parameter of a callback prop.</summary>
internal sealed record SnapshotArg(string Name, bool Optional, SnapshotType Type);
