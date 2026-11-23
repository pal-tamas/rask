namespace Rask.Data;

/// <summary>An aggregate's "has a row" mark and <c>Version</c> before a save (<see cref="AttemptUndo" />).</summary>
internal readonly record struct AttemptState(bool Stored, int Version);
