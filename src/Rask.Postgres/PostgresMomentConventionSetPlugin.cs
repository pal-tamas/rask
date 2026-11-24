using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Stores a <c>Moment</c> value object as the UTC instant it is: Npgsql maps a <see cref="DateTime" /> to
///     <c>timestamptz</c> and refuses a wall-clock time, so the column holds UTC and the value object its wall-clock time,
///     converted through <c>Clock.Zone</c> both ways — the moment SQL Server and SQLite keep as written.
/// </summary>
internal sealed class PostgresMomentConventionSetPlugin : IConventionSetPlugin
{
    // First of the finalizing conventions: EF Core picks each column's type mapping from its converter after it.
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.ModelFinalizingConventions.Insert(0, new UtcMomentConvention());
        return conventionSet;
    }
}
