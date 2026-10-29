namespace Rask.Data;

/// <summary>A delete keeps the row: <c>X.Delete(id)</c> stamps <c>DeletedAt</c> instead of removing it, and every read hides it; <c>IgnoreQueryFilters()</c> lists it again.</summary>
/// <remarks>
/// Every aggregate has a generated delete; this trait only decides what it does. Without it the row and its
/// children are removed.
/// </remarks>
/// <remarks>
/// Implemented by what the aggregate inherits: <see cref="Aggregate{TId}.DeletedAt" /> fills it. A child entity has
/// no <c>DeletedAt</c>, so it cannot implement this one.
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>When the aggregate was deleted, in UTC, or <c>null</c>.</summary>
    DateTime? DeletedAt { get; }
}
