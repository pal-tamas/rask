namespace Rask.Data;

/// <summary>The aggregate has a <c>Version</c> concurrency token: a save built from a stale copy is refused.</summary>
/// <remarks>
/// Implemented by what the aggregate inherits: <see cref="Aggregate{TId}.Version" /> fills it. A child entity has no
/// <c>Version</c>, so it cannot implement this one.
/// </remarks>
public interface IVersioned
{
    /// <summary>The optimistic-concurrency token, bumped on every save of the aggregate.</summary>
    int Version { get; }
}
