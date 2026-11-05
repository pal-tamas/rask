using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace Rask.Data;

/// <summary>Teaches the model what <see cref="TextOrderExtensions.InTextOrder" /> is written as in SQL.</summary>
/// <remarks>
///     A function of the model, not a column's facet: the collation reaches the <c>ORDER BY</c> that asks for it and
///     nothing else — no column, no migration, no <c>WHERE</c>. Read once, as the model is built.
/// </remarks>
/// <param name="collation">The SQL Server collation, or null on a database where the name means nothing.</param>
internal sealed class TextOrderTranslationConvention(string? collation) : IModelInitializedConvention
{
    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var function = modelBuilder.HasDbFunction(new Func<string, string>(TextOrderExtensions.InTextOrder).Method);

        // The name is SQL Server's: another database would refuse it, or read it as one of its own — so there the
        // column is ordered as it is.
        function?.HasTranslation(collation is { } name
            ? arguments => new CollateExpression(arguments[0], name)
            : static arguments => arguments[0]);
    }
}
