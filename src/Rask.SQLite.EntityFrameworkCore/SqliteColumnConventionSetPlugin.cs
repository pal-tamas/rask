using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Rask.SQLite;

/// <summary>
///     Adds the conventions that make a <c>UseRaskSqlite</c> column hold its value as a server's would —
///     <see cref="MomentOrderConvention" /> and <see cref="DecimalScaleConvention" /> — ahead of EF Core's own.
/// </summary>
internal sealed class SqliteColumnConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        // First, so each converter is the property's before EF picks its type mapping, and the moment collation is set
        // before a convention that collates every text column (OrderTextBy's) would read the converted column as text.
        conventionSet.ModelFinalizingConventions.Insert(0, new MomentOrderConvention());
        conventionSet.ModelFinalizingConventions.Insert(1, new DecimalScaleConvention());
        return conventionSet;
    }
}
