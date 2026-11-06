using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Rask.Data;

/// <summary>The execution half a <see cref="ModelQueryable{TElement}" /> calls back into.</summary>
internal interface IModelQueryProvider : IAsyncQueryProvider
{
    /// <summary>The element type of the model query this provider runs.</summary>
    Type ElementType { get; }

    List<TElement> Materialize<TElement>(Expression expression);

    IAsyncEnumerable<TElement> Stream<TElement>(Expression expression, CancellationToken cancellationToken);

    /// <summary>The model query applied to <paramref name="context" />'s no-tracking set — what its root stands for.</summary>
    Expression SourceIn(DbContext context);
}
