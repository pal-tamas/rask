using Microsoft.EntityFrameworkCore;
using ExcavatorId = Rask.Data.Tests.Shapes.Referenced.ExcavatorId;

namespace Rask.Data.Tests.Shapes.HandReferenced;

// Linked into Rask.Providers.E2E.Tests as well, so the move to BelongsTo is compared on every provider. The same names as
// Shapes.Referenced, in a namespace of their own: the tables, the keys and the constraints are named after them.

/// <summary>A quarry, as an app writes it by hand.</summary>
public sealed class Quarry
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>An excavator holding its quarry's id and a navigation to it, written by hand.</summary>
public sealed class Excavator
{
    public ExcavatorId Id { get; set; }

    public string Make { get; set; } = "";

    public Guid? QuarryId { get; set; }

    public Quarry? Quarry { get; set; }
}

/// <summary>A dig holding its excavator's id and navigation, and the id of the quarry its spoil went to.</summary>
public sealed class Dig
{
    public Guid Id { get; set; }

    public decimal Tonnes { get; set; }

    public ExcavatorId ExcavatorId { get; set; }

    public Excavator Excavator { get; set; } = null!;

    public Guid? DumpedAtId { get; set; }
}

/// <summary>
///     The hand-written model: the excavator's quarry by EF Core's convention, the dig's excavator configured as the TA and
///     Eszaf models configure theirs — <c>HasOne&lt;T&gt;().WithMany().HasForeignKey(…).OnDelete(Restrict)</c> — and its
///     dump configured with EF Core's own delete.
/// </summary>
public sealed class HandReferencedQuarryContext(DbContextOptions<HandReferencedQuarryContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quarry>();
        modelBuilder.Entity<Excavator>();
        var dig = modelBuilder.Entity<Dig>();
        dig.HasOne(d => d.Excavator).WithMany().HasForeignKey(d => d.ExcavatorId).OnDelete(DeleteBehavior.Restrict);
        dig.HasOne<Quarry>().WithMany().HasForeignKey(d => d.DumpedAtId);
        modelBuilder.ApplyRaskConventions();
    }
}
