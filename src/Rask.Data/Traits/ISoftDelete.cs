namespace Rask.Data;

/// <summary>The aggregate gets a generated <c>X.Delete(id)</c> that stamps <c>DeletedAt</c> instead, and every read hides the row; <c>IgnoreQueryFilters()</c> lists it again.</summary>
/// <remarks>
/// Implemented by what the aggregate inherits: <see cref="Aggregate{TId}.DeletedAt" /> fills it. A child entity has
/// no <c>DeletedAt</c>, so it cannot implement this one.
/// </remarks>
public interface ISoftDelete
{
    /// <summary>When the aggregate was deleted, in UTC, or <c>null</c>.</summary>
    DateTime? DeletedAt { get; }
}
