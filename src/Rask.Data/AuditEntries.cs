using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>Maps <see cref="AuditEntry" /> into a model that has an <see cref="Audited" /> aggregate, and only into one.</summary>
internal static class AuditEntries
{
    /// <summary>The table the entries are written to.</summary>
    internal const string Table = "Audits";

    internal static void Map(ModelBuilder modelBuilder, List<IMutableEntityType> entityTypes)
    {
        if (!entityTypes.Exists(static e => ConventionRegistry.Has(e.ClrType, TraitSet.Audited)))
        {
            return;
        }

        // Its names are Rask's, as every battery table's are: the app's TableNames and ColumnNames leave them.
        KeptNames.Keep(typeof(AuditEntry));
        modelBuilder.Entity<AuditEntry>(entry =>
        {
            entry.ToTable(Table);
            entry.HasKey(a => a.Id);
            entry.Property(a => a.Id).ValueGeneratedOnAdd();
            entry.Property(a => a.Aggregate).HasMaxLength(128);
            entry.Property(a => a.Action).HasConversion<string>().HasMaxLength(16);
            entry.Property(a => a.Key).HasMaxLength(450);
            entry.Property(a => a.User).HasMaxLength(128);
            entry.HasIndex(a => new { a.Aggregate, a.Key });
        });
    }

    /// <summary>Whether <paramref name="model" /> writes audit entries at all.</summary>
    internal static bool AreMapped(IModel model) => model.FindEntityType(typeof(AuditEntry)) is not null;
}
