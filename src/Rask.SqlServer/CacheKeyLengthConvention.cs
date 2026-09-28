using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.SqlServer;

/// <summary>
/// Fits <c>Rask.Cache</c>'s key inside SQL Server's index key limit.
/// </summary>
/// <remarks>
/// <para>
/// A clustered index key holds at most 900 bytes, and <c>nvarchar</c> spends two per character, so a string key
/// longer than 450 characters is one SQL Server creates with a warning and then refuses to insert into. EF's own
/// SQL Server mapping already caps an unconfigured string key at <c>nvarchar(450)</c> for exactly this reason —
/// but <c>CacheEntry.Key</c> is configured at 512 so that SQLite and PostgreSQL keep their room, and an explicit
/// length wins over the provider's default.
/// </para>
/// <para>
/// So the cap is applied here, by the SQL Server package, and only to that key: SQLite apps get no migration out
/// of it, and no application entity is ever silently re-sized. The type is matched by name to keep this package
/// free of a <c>Rask.Cache</c> dependency; a test pins the name.
/// </para>
/// </remarks>
internal sealed class CacheKeyLengthConvention : IModelFinalizingConvention
{
    internal const string CacheEntryTypeName = "Rask.Caching.CacheEntry";

    internal const int MaxKeyLength = 450;

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!string.Equals(entityType.ClrType.FullName, CacheEntryTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            // Every INDEXED string on the row, not just the primary key's. The cache key stopped being the
            // primary key when CacheEntry took a surrogate Id — it is a UNIQUE index now — and SQL Server's
            // 900-byte limit applies to an index key whether or not it is the primary one. Looking only at the
            // primary key would have left an nvarchar(512) unique index that SQL Server creates with a warning
            // and then refuses to insert into.
            var indexed = entityType.GetIndexes().SelectMany(static i => i.Properties)
                .Concat(entityType.FindPrimaryKey()?.Properties ?? []);

            foreach (var property in indexed)
            {
                // The length was set explicitly by Rask.Cache's own configuration, which a convention-sourced
                // SetMaxLength would not override; the mutable surface sets it at explicit precedence.
                if (property.ClrType == typeof(string) && property.GetMaxLength() is > MaxKeyLength)
                {
                    ((IMutableProperty)property).SetMaxLength(MaxKeyLength);
                }
            }
        }
    }
}
