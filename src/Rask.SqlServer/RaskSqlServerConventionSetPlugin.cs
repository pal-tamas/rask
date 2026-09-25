using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Rask.SqlServer;

/// <summary>Adds <see cref="CacheKeyLengthConvention"/> to the model's conventions.</summary>
internal sealed class RaskSqlServerConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new CacheKeyLengthConvention());
        return conventionSet;
    }
}
