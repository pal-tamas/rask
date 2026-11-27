using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Stores a <c>Moment</c> value object as the UTC instant it is: Npgsql maps a <see cref="DateTime" /> to
///     <c>timestamptz</c> and refuses a wall-clock time, so the column holds the instant the moment holds, in UTC — as SQL
///     Server and SQLite now keep it too.
/// </summary>
internal sealed class PostgresMomentConventionSetPlugin : IConventionSetPlugin
{
    // First of the finalizing conventions: EF Core picks each column's type mapping from its converter after it.
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        // PostgreSQL kept moments as UTC from the start: there is nothing to rewrite.
        conventionSet.ModelFinalizingConventions.Insert(0, new UtcMomentConvention(rewritesWallClock: false));

        // Last of the entity-added conventions: after [NotMapped] set the stamps aside and discovery took a Timestamp for a
        // navigation, both of which it overrides (TimestampConvention).
        if (!conventionSet.EntityTypeAddedConventions.OfType<TimestampConvention>().Any())
        {
            var timestamps = new TimestampConvention();
            conventionSet.EntityTypeAddedConventions.Add(timestamps);
            conventionSet.ModelFinalizingConventions.Insert(0, timestamps);
        }
        return conventionSet;
    }
}
