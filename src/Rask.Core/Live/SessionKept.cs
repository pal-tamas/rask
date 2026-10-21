namespace Rask.Core.Live;

/// <summary>A scoped service an event takes from its session (<see cref="EventScopeCatalog.KeepForSession" />).</summary>
internal sealed record SessionKept(Type Type);
