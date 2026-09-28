namespace Rask.Generators.External.PackageIslands;

/// <summary>A named object type a snapshot declares once and refers to by <c>ref</c>.</summary>
internal sealed record SnapshotNamedType(string Name, SnapshotType Type);
