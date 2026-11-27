using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Adds <see cref="UtcMomentConvention" /> to every Rask context's model, whatever its provider: a moment is stored as
///     its UTC instant on SQL Server — whose package does not reference this one — as on SQLite and PostgreSQL, which also
///     add it themselves for a context that is not a Rask one. Where two add it, the second finds it done.
/// </summary>
/// <param name="provider">The context's database provider: PostgreSQL kept moments as UTC from the start, the others as the wall clock.</param>
internal sealed class UtcMomentConventionSetPlugin(IDatabaseProvider provider) : IConventionSetPlugin
{
    private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

    // First of the finalizing conventions: EF Core picks each column's type mapping from its converter after it.
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var rewrites = !string.Equals(provider.Name, Npgsql, StringComparison.Ordinal);
        conventionSet.ModelFinalizingConventions.Insert(0, new UtcMomentConvention(rewritesWallClock: rewrites));
        return conventionSet;
    }
}
