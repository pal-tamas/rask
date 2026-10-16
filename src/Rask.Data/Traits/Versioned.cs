namespace Rask.Data;

/// <summary>The aggregate has a <c>Version</c> concurrency token: a save built from a stale copy is refused.</summary>
public abstract class Versioned : IAggregateTrait;
