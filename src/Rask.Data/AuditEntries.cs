using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Maps <see cref="AuditEntry" /> into a model that has an <see cref="Audited" /> aggregate, and only into one — unless
///     the app named a format of its own, which writes its own rows (<see cref="AuditBuilderExtensions.AuditAs{TRow, TUserId}" />).
/// </summary>
internal static class AuditEntries
{
    /// <summary>The table the entries are written to.</summary>
    internal const string Table = "Audits";

    /// <summary>The model annotation that holds the app's own <see cref="AuditFormat{TRow, TUserId}" />.</summary>
    internal const string FormatAnnotation = "Rask:AuditFormat";

    internal static void Map(ModelBuilder modelBuilder, List<IMutableEntityType> entityTypes)
    {
        if (OwnFormat(modelBuilder.Model) is not null ||
            !entityTypes.Exists(static e => ConventionRegistry.Has(e.ClrType, TraitSet.Audited)))
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

    /// <summary>
    ///     The format <paramref name="model" />'s saves are recorded with: the app's own when its model named one,
    ///     <paramref name="registered" /> when it maps <see cref="AuditEntry" />, and null when it records nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">An app's own row format was registered as a service instead of named in the model.</exception>
    internal static AuditFormat? FormatFor(IModel model, AuditFormat registered)
    {
        if (OwnFormat(model) is { } own)
        {
            return own;
        }

        if (model.FindEntityType(typeof(AuditEntry)) is null)
        {
            return null;
        }

        return registered.RowType == typeof(AuditEntry)
            ? registered
            : throw new InvalidOperationException(
                $"{registered.GetType().Name} writes {registered.RowType.Name} rows, which only the model can map: name it in the " +
                $"app's IRaskModel rather than as a service — public static void Model(ModelBuilder m) => m.AuditAs(new {registered.GetType().Name}());");
    }

    private static AuditFormat? OwnFormat(IReadOnlyAnnotatable model) => model.FindAnnotation(FormatAnnotation)?.Value as AuditFormat;
}
