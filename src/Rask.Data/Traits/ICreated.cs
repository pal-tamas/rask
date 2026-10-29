namespace Rask.Data;

/// <summary>The table has a <c>CreatedAt</c>, stamped when the row is added.</summary>
/// <remarks>
/// Implemented by what the type inherits: <see cref="Entity{TId}.CreatedAt" /> fills it. A child entity may ask for
/// it too — <c>OrderLine : Entity&lt;Guid&gt;, ICreated</c>.
/// </remarks>
public interface ICreated
{
    /// <summary>When the row was inserted, in UTC.</summary>
    DateTime CreatedAt { get; }
}
