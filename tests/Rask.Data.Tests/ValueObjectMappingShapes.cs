using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data.Tests.Shapes;

// Linked into Rask.Providers.E2E.Tests as well, so the same two mappings are compared on every provider.

public sealed record ShapeLabel(string Value);

public sealed record ShapeCount(int Value);

public sealed record ShapeAmount(decimal Value);

public sealed record ShapeDay(DateOnly Value);

public sealed record ShapeRef(Guid Value);

public sealed record ShapeNote(string Value);

/// <summary>One row holding a one-value value object of each inner type a column can be, and a nullable one.</summary>
public sealed class ShapeParcel
{
    public int Id { get; set; }

    public ShapeLabel Label { get; set; } = new("");

    public ShapeCount Count { get; set; } = new(0);

    public ShapeAmount Price { get; set; } = new(0m);

    public ShapeDay Day { get; set; } = new(default);

    public ShapeRef Ref { get; set; } = new(default);

    public ShapeNote? Note { get; set; }

    public int? TenantId { get; set; }
}

/// <summary>How a one-value value object was mapped: a complex property whose one column takes the property's name.</summary>
public sealed class ComplexShapeContext(DbContextOptions<ComplexShapeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ShapeParcel>(e =>
        {
            e.ToTable("Parcels");
            e.ComplexProperty(x => x.Label, b => b.Property(v => v.Value).HasColumnName("Label").HasMaxLength(255));
            e.ComplexProperty(x => x.Count, b => b.Property(v => v.Value).HasColumnName("Count"));
            e.ComplexProperty(x => x.Price, b => b.Property(v => v.Value).HasColumnName("Price").HasPrecision(18, 9));
            e.ComplexProperty(x => x.Day, b => b.Property(v => v.Value).HasColumnName("Day"));
            e.ComplexProperty(x => x.Ref, b => b.Property(v => v.Value).HasColumnName("Ref"));
            e.ComplexProperty(x => x.Note, b => b.Property(v => v.Value).HasColumnName("Note"));
        });
}

/// <summary>The same row with each value object as a converted scalar column.</summary>
public sealed class ScalarShapeContext(DbContextOptions<ScalarShapeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ShapeParcel>(e =>
        {
            e.ToTable("Parcels");
            e.Property(x => x.Label).HasConversion(v => v.Value, s => new ShapeLabel(s)).HasMaxLength(255);
            e.Property(x => x.Count).HasConversion(v => v.Value, s => new ShapeCount(s));
            e.Property(x => x.Price).HasConversion(v => v.Value, s => new ShapeAmount(s)).HasPrecision(18, 9);
            e.Property(x => x.Day).HasConversion(v => v.Value, s => new ShapeDay(s));
            e.Property(x => x.Ref).HasConversion(v => v.Value, s => new ShapeRef(s));
            e.Property(x => x.Note).HasConversion(v => v!.Value, s => new ShapeNote(s));
        });
}

/// <summary>What EF Core's own migration differ says moving a database from one mapping to the other would take.</summary>
public static class ValueObjectMappingDiff
{
    /// <summary>The operations a migration from the complex mapping to the scalar one would hold, and back.</summary>
    public static (IReadOnlyList<string> Forward, IReadOnlyList<string> Back) Between(
        DbContext complex, DbContext scalar)
    {
        ArgumentNullException.ThrowIfNull(complex);
        ArgumentNullException.ThrowIfNull(scalar);

        var from = complex.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var to = scalar.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        return (
            Describe(scalar.GetService<IMigrationsModelDiffer>().GetDifferences(from, to)),
            Describe(complex.GetService<IMigrationsModelDiffer>().GetDifferences(to, from)));
    }

    /// <summary>Each column of the table as the provider would create it, in name order.</summary>
    public static IReadOnlyList<string> Columns(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return [.. context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == "Parcels").Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name} {c.StoreType} {(c.IsNullable ? "NULL" : "NOT NULL")} default={c.DefaultValueSql ?? c.DefaultValue?.ToString() ?? "-"}")];
    }

    /// <summary>The columns in the order a fresh <c>CREATE TABLE</c> would list them.</summary>
    public static IReadOnlyList<string> CreateOrder(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var create = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, context.GetService<IDesignTimeModel>().Model.GetRelationalModel())
            .OfType<CreateTableOperation>()
            .Single(t => t.Name == "Parcels");

        return [.. create.Columns.Select(c => c.Name)];
    }

    private static List<string> Describe(IReadOnlyList<MigrationOperation> operations) =>
        [.. operations.Select(o => o switch
        {
            AlterColumnOperation a => $"AlterColumn {a.Table}.{a.Name}: {a.OldColumn.ColumnType} {(a.OldColumn.IsNullable ? "NULL" : "NOT NULL")} -> {a.ColumnType} {(a.IsNullable ? "NULL" : "NOT NULL")}",
            _ => o.GetType().Name,
        })];
}
