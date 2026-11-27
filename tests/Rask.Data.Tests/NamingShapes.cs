using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests.NamingShapes;

// One model in two namings: Rask's own, and what an app had before it — EF Core's, and a few names stated by hand. Shared
// with Rask.Providers.E2E.Tests, which proves on SQL Server and PostgreSQL that moving from one to the other is renames.

public sealed record Odometer(int Start, int End);

public sealed class Pistol
{
    public int Id { get; set; }

    public int Number { get; set; }

    public string Label { get; set; } = "";
}

public sealed class Fueling
{
    public int Id { get; set; }

    public int PistolId { get; set; }

    public Pistol? Pistol { get; set; }

    public DateTime CreatedAt { get; set; }

    public decimal Litres { get; set; }

    public int Flagged { get; set; }

    public Odometer Km { get; set; } = new(0, 0);
}

/// <summary>A row keyed by two columns.</summary>
public sealed class TankLevel
{
    public int TankId { get; set; }

    public DateTime CountedAt { get; set; }

    public int Litres { get; set; }
}

/// <summary>A reading whose key to its pistol is a shadow property: <c>PistolId</c> in EF Core, <c>pistol_id</c> in Rask.</summary>
public sealed class Reading
{
    public int Id { get; set; }

    public Pistol? Pistol { get; set; }
}

public sealed class Address
{
    public string Street { get; set; } = "";
}

public sealed class Leg
{
    public int Id { get; set; }

    public int DrivenKm { get; set; }
}

/// <summary>An owner of one value in its own table, and of a list in a table of their own.</summary>
public sealed class Depot
{
    public int Id { get; set; }

    public Address Address { get; set; } = new();

    public List<Leg> Legs { get; set; } = [];
}

/// <summary>A hierarchy in one table, with its discriminator.</summary>
public class Nozzle
{
    public int Id { get; set; }

    public int Number { get; set; }
}

public sealed class IbcNozzle : Nozzle
{
    public int LitresLeft { get; set; }
}

/// <summary>Both sides of a many-to-many, with the join table EF Core makes.</summary>
public sealed class VehicleCategory
{
    public int Id { get; set; }

    public List<Vehicle> Vehicles { get; set; } = [];
}

public sealed class Vehicle
{
    public int Id { get; set; }

    public List<VehicleCategory> VehicleCategories { get; set; } = [];
}

public sealed class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>A class name long enough that its constraints' names pass PostgreSQL's 63 bytes.</summary>
public sealed class MonthlyExciseDutyReturnForAgriculturalDieselRefundClaimLine
{
    public int Id { get; set; }

    public int AgriculturalHoldingRegistrationNumberAssignedByTheAuthority { get; set; }
}

/// <summary>The model both namings map.</summary>
public static class NamingModel
{
    public static void Map(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pistol>().HasAlternateKey(p => p.Number);
        modelBuilder.Entity<Fueling>(Fueling);
        modelBuilder.Entity<TankLevel>().HasKey(t => new { t.TankId, t.CountedAt });
        modelBuilder.Entity<Reading>().HasOne(r => r.Pistol).WithMany();
        modelBuilder.Entity<Depot>(depot =>
        {
            depot.OwnsOne(d => d.Address);
            depot.OwnsMany(d => d.Legs);
        });
        modelBuilder.Entity<Nozzle>();
        modelBuilder.Entity<IbcNozzle>();
        modelBuilder.Entity<VehicleCategory>().HasMany(c => c.Vehicles).WithMany(v => v.VehicleCategories);
        modelBuilder.Entity<Person>().HasIndex(p => p.Name);
        modelBuilder.Entity<MonthlyExciseDutyReturnForAgriculturalDieselRefundClaimLine>()
            .HasIndex(l => l.AgriculturalHoldingRegistrationNumberAssignedByTheAuthority);
    }

    private static void Fueling(EntityTypeBuilder<Fueling> fueling)
    {
        fueling.ComplexProperty(f => f.Km);
        fueling.HasIndex(f => f.CreatedAt);
        fueling.HasIndex(f => new { f.PistolId, f.CreatedAt }).IsUnique();
        fueling.Property(f => f.Flagged).HasDefaultValue(0);
    }
}

/// <summary>The model named as Rask names it, through the conventions every Rask context applies.</summary>
public sealed class RaskNamedContext(DbContextOptions<RaskNamedContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => NamingModel.Map(modelBuilder);
}

/// <summary>
///     The model as an app had it before: EF Core's names, the fuelings stated by hand the way a Prisma schema names them.
/// </summary>
public sealed class LegacyNamedContext(DbContextOptions<LegacyNamedContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        NamingModel.Map(modelBuilder);
        modelBuilder.Entity<Fueling>(fueling =>
        {
            fueling.ToTable("Fueling");
            fueling.Property(f => f.PistolId).HasColumnName("pistolId");
            fueling.Property(f => f.CreatedAt).HasColumnName("createdAt");
            fueling.HasIndex(f => f.CreatedAt).HasDatabaseName("Fueling_createdAt_idx");
            fueling.HasKey(f => f.Id).HasName("Fueling_pkey");
        });
    }
}
