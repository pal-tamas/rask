using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>The execution half a <see cref="ModelQueryable{TElement}" /> calls back into.</summary>
internal interface IModelQueryProvider : IAsyncQueryProvider
{
    List<TElement> Materialize<TElement>(Expression expression);

    IAsyncEnumerable<TElement> Stream<TElement>(Expression expression, CancellationToken cancellationToken);
}
