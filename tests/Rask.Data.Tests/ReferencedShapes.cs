using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Shapes.Referenced;

// Linked into Rask.Providers.E2E.Tests as well: Shapes.HandReferenced's quarry, its references now BelongsTo.

/// <summary>An excavator's key, a typed id over a <see cref="Guid" />.</summary>
public readonly partial record struct ExcavatorId(Guid Value);

/// <summary>A quarry: an aggregate others refer to.</summary>
public sealed class Quarry : Aggregate<Guid>
{
    private Quarry() { } // EF materialization

    public string Name { get; private set; } = "";

    internal static Quarry Named(string name) => new() { Id = Uuid.Create(), Name = name };
}

/// <summary>An excavator, kept at one quarry or none.</summary>
public sealed partial class Excavator : Aggregate<ExcavatorId>
{
    private Excavator() { } // EF materialization

    public string Make { get; private set; } = "";

    public partial BelongsTo<Quarry>? Quarry { get; }

    internal static Excavator Made(string make, Guid? quarry) =>
        new() { Id = ExcavatorId.Create(), Make = make, QuarryId = quarry };
}

/// <summary>A dig: always by one excavator, its spoil dumped at a quarry or not yet.</summary>
public sealed partial class Dig : Aggregate<Guid>
{
    private Dig() { } // EF materialization

    public decimal Tonnes { get; private set; }

    public partial BelongsTo<Excavator> Excavator { get; }

    public partial BelongsTo<Quarry>? DumpedAt { get; }

    internal static Dig By(ExcavatorId excavator, decimal tonnes) =>
        new() { Id = Uuid.Create(), ExcavatorId = excavator, Tonnes = tonnes };

    public void Dump(Guid quarry) => DumpedAtId = quarry;
}

/// <summary>
///     The quarry's model under Rask's conventions: each BelongsTo mapped as the relationship it stands for, the dig's
///     excavator keeping the <c>OnDelete(Restrict)</c> its hand-written model stated, as an app keeps that line.
/// </summary>
public sealed class ReferencedQuarryContext(DbContextOptions<ReferencedQuarryContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quarry>();
        modelBuilder.Entity<Excavator>();
        var dig = modelBuilder.Entity<Dig>();
        modelBuilder.ApplyRaskConventions();
        dig.HasOne<Excavator>().WithMany().HasForeignKey(d => d.ExcavatorId).OnDelete(DeleteBehavior.Restrict);
    }
}
