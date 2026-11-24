using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Rask.SQLite;

/// <summary>Adds <see cref="MomentOrderConvention" /> to the model's conventions, ahead of EF Core's own.</summary>
internal sealed class MomentOrderConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        // First, so the converter is the property's before EF picks its type mapping, and the collation is set before a
        // convention that collates every text column (OrderTextBy's) would read the converted column as text.
        conventionSet.ModelFinalizingConventions.Insert(0, new MomentOrderConvention());
        return conventionSet;
    }
}
