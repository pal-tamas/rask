using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A literal of a generated enum, with the member name it got.</summary>
internal sealed record GeneratedEnumMember(string Member, SnapshotLiteral Literal);
