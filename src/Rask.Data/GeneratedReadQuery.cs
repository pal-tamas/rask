using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>
///     Opens a query over a generated read face, for the generated <c>Order.Read</c> to return.
/// </summary>
/// <remarks>
///     <see cref="ModelQuery{TEntity}" /> is constructed inside Rask.Data, and the read faces are generated
///     into the application's own assembly — so there has to be one public door for them to come through.
///     Not a type to call: <c>Order.Read</c> is what an application writes.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedReadQuery
{
    /// <summary>The whole set of <typeparamref name="TRead" />, as a query that has not run yet.</summary>
    public static ModelQuery<TRead> Of<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead>()
        where TRead : class, IReadModel => new();
}
