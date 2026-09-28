using System.ComponentModel;

namespace Rask.Data;

/// <summary>What every <see cref="Entity{TId}" /> is, for code that is generic over entities of any key type.</summary>
/// <remarks>
/// Not a type to implement: derive from <see cref="Entity{TId}" /> or <see cref="Aggregate{TId}" />. It exists
/// because C# cannot infer <c>TId</c> from <c>Product.Where(…)</c>, so the generic surface needs something
/// non-generic to constrain on.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEntity;
