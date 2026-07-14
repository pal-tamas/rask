namespace Rask.Cli.Scaffolding;

/// <summary>A single file a generator wants to write: its absolute <see cref="Path"/> and <see cref="Content"/>.</summary>
internal sealed record ScaffoldFile(string Path, string Content);
