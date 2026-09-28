namespace Rask.Generators.External.PackageIslands;

/// <summary>One literal of an enum-like union: a string, a number or a boolean, as written.</summary>
internal sealed record SnapshotLiteral(string Text, bool IsNumber, bool IsBoolean);
