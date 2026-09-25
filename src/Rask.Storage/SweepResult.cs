namespace Rask.Storage;

/// <summary>What one sweep did.</summary>
internal readonly record struct SweepResult(int Deleted, int Orphans, bool Tripped);
