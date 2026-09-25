namespace Rask.Generators.External.PackageIslands;

/// <summary>A prop the extractor saw and deliberately did not describe.</summary>
internal sealed record SnapshotSkip(string Name, string Reason, string? Detail);
