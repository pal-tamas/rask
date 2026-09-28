namespace Rask.Generators.External.PackageIslands;

/// <summary>A member of an object type in a props snapshot.</summary>
internal sealed record SnapshotMember(string Name, bool Required, string? Doc, SnapshotType Type);
