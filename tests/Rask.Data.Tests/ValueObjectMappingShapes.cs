using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

public readonly record struct ShapeWeight(int Value);

/// <summary>A value object of several values, built through its members, holding one-value value objects.</summary>
public sealed class ShapeCrate
{
    public ShapeLabel Label { get; set; } = new("");

    public ShapeNote? Note { get; set; }

    public ShapeAmount Price { get; set; } = new(0m);

    public ShapeWeight Weight { get; set; }

    public ShapeWeight? Tare { get; set; }

    public int Units { get; set; }
}

/// <summary>Holds the crate, so its one-value value objects sit two levels down.</summary>
public sealed class ShapeHold
{
    public ShapeCrate Crate { get; set; } = new();

    public string Deck { get; set; } = "";
}

/// <summary>One row holding one-value value objects one level and two levels inside other value objects.</summary>
public sealed class ShapeCargo
{
    public int Id { get; set; }

    public ShapeCrate Crate { get; set; } = new();

    public ShapeHold Hold { get; set; } = new();
}

/// <summary>How a one-value value object INSIDE another was mapped: a complex type in a complex type.</summary>
public sealed class ComplexNestedShapeContext(DbContextOptions<ComplexNestedShapeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ShapeCargo>(e =>
        {
            e.ToTable(ValueObjectMappingDiff.NestedTable);
            e.ComplexProperty(x => x.Crate, b0 => Crate(b0, "Crate"));
            e.ComplexProperty(x => x.Hold, b0 => b0.ComplexProperty(x => x.Crate, b1 => Crate(b1, "Hold_Crate")));
        });

    private static void Crate(ComplexPropertyBuilder<ShapeCrate> crate, string prefix)
    {
        crate.ComplexProperty(x => x.Label, b => b.Property(v => v.Value).HasColumnName(prefix + "_Label").HasMaxLength(255));
        crate.ComplexProperty(x => x.Note, b => b.Property(v => v.Value).HasColumnName(prefix + "_Note"));
        crate.ComplexProperty(x => x.Price, b => b.Property(v => v.Value).HasColumnName(prefix + "_Price").HasPrecision(18, 9));
        crate.ComplexProperty(x => x.Weight, b => b.Property(v => v.Value).HasColumnName(prefix + "_Weight"));
        crate.ComplexProperty<ShapeWeight>(x => x.Tare, b => b.Property(v => v.Value).HasColumnName(prefix + "_Tare"));
    }
}

/// <summary>The same row with each of them as a converted scalar column of the value object that holds it.</summary>
public sealed class ScalarNestedShapeContext(DbContextOptions<ScalarNestedShapeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ShapeCargo>(e =>
        {
            e.ToTable(ValueObjectMappingDiff.NestedTable);
            e.ComplexProperty(x => x.Crate, b0 => Crate(b0, "Crate"));
            e.ComplexProperty(x => x.Hold, b0 => b0.ComplexProperty(x => x.Crate, b1 => Crate(b1, "Hold_Crate")));
        });

    private static void Crate(ComplexPropertyBuilder<ShapeCrate> crate, string prefix)
    {
        crate.Property(x => x.Label).HasConversion(v => v.Value, s => new ShapeLabel(s)).HasColumnName(prefix + "_Label").HasMaxLength(255);
        crate.Property(x => x.Note).HasConversion(v => v!.Value, s => new ShapeNote(s)).HasColumnName(prefix + "_Note");
        crate.Property(x => x.Price).HasConversion(v => v.Value, s => new ShapeAmount(s)).HasColumnName(prefix + "_Price").HasPrecision(18, 9);
        crate.Property(x => x.Weight).HasConversion(v => v.Value, s => new ShapeWeight(s)).HasColumnName(prefix + "_Weight");
        crate.Property(x => x.Tare).HasConversion(v => v!.Value.Value, s => new ShapeWeight(s)).HasColumnName(prefix + "_Tare");
    }
}

/// <summary>What EF Core's own migration differ says moving a database from one mapping to the other would take.</summary>
public static class ValueObjectMappingDiff
{
    /// <summary>The table of the row that holds value objects inside value objects.</summary>
    public const string NestedTable = "Cargos";

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
    public static IReadOnlyList<string> Columns(DbContext context, string table = "Parcels")
    {
        ArgumentNullException.ThrowIfNull(context);

        return [.. context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables
            .Single(t => t.Name == table).Columns
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => $"{c.Name} {c.StoreType} {(c.IsNullable ? "NULL" : "NOT NULL")} default={c.DefaultValueSql ?? c.DefaultValue?.ToString() ?? "-"}")];
    }

    /// <summary>The columns in the order a fresh <c>CREATE TABLE</c> would list them.</summary>
    public static IReadOnlyList<string> CreateOrder(DbContext context, string table = "Parcels")
    {
        ArgumentNullException.ThrowIfNull(context);

        var create = context.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, context.GetService<IDesignTimeModel>().Model.GetRelationalModel())
            .OfType<CreateTableOperation>()
            .Single(t => t.Name == table);

        return [.. create.Columns.Select(c => c.Name)];
    }

    private static List<string> Describe(IReadOnlyList<MigrationOperation> operations) =>
        [.. operations.Select(o => o switch
        {
            AlterColumnOperation a => $"AlterColumn {a.Table}.{a.Name}: {a.OldColumn.ColumnType} {(a.OldColumn.IsNullable ? "NULL" : "NOT NULL")} -> {a.ColumnType} {(a.IsNullable ? "NULL" : "NOT NULL")}",
            _ => o.GetType().Name,
        })];
}
