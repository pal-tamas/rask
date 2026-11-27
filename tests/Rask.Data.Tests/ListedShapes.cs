using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Shapes.Listed;

// Linked into Rask.Providers.E2E.Tests as well: Shapes.HandListed's rack, its shelves now a HasMany.

/// <summary>The rack of Shapes.HandListed, its shelves one <see cref="HasMany{TChild}" />.</summary>
public sealed class Rack
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public HasMany<Shelf> Shelves { get; } = new();
}

/// <summary>A rack's shelf.</summary>
public sealed class Shelf : IEntity
{
    public Guid Id { get; set; }

    public int Slots { get; set; }
}

/// <summary>The rack and its shelves under Rask's conventions.</summary>
public sealed class ListedRackContext(DbContextOptions<ListedRackContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rack>();
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>The same, the shelves kept in the order the rack gives them.</summary>
public sealed class OrderedRackContext(DbContextOptions<OrderedRackContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var rack = modelBuilder.Entity<Rack>();
        modelBuilder.ApplyRaskConventions();
        rack.InOrder(r => r.Shelves);
    }
}
