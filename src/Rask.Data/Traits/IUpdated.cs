namespace Rask.Data;

/// <summary>The table has an <c>UpdatedAt</c>, stamped on every save.</summary>
/// <remarks>
/// Implemented by what the type inherits: <see cref="Entity{TId}.UpdatedAt" /> fills it. A child entity may ask for
/// it too — <c>OrderLine : Entity&lt;Guid&gt;, IUpdated</c>.
/// </remarks>
public interface IUpdated
{
    /// <summary>When the row last changed, in UTC.</summary>
    DateTime UpdatedAt { get; }
}
