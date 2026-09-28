using System.ComponentModel;

namespace Rask.Data;

/// <summary>What every <see cref="Aggregate{TId}" /> is, for code that is generic over aggregates of any key type.</summary>
/// <remarks>Not a type to implement: derive from <see cref="Aggregate{TId}" />. See <see cref="IEntity" />.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAggregate : IEntity;
