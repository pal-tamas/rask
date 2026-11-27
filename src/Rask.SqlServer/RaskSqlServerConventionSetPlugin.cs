using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Rask.Data;

namespace Rask.SqlServer;

/// <summary>
///     Adds <see cref="CacheKeyLengthConvention"/> and <see cref="SqlServerGuidOrderConvention"/> to the model's
///     conventions: every <c>UseRaskSqlServer</c> context stores its <c>uniqueidentifier</c> columns by time, a plain
///     <c>DbContext</c> holding a battery's tables too.
/// </summary>
/// <param name="services">EF Core's internal service provider, which the Guid order is decided from.</param>
internal sealed class RaskSqlServerConventionSetPlugin(IServiceProvider services) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new CacheKeyLengthConvention());

        // A model built with Rask.Data's conventions is left to theirs, which keep a one-value value object's converter.
        conventionSet.Add(SqlServerGuidOrderConvention.For(services, StoredGuidConverter.After, yieldsToRaskConventions: true));
        return conventionSet;
    }
}
