using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Shapes.HandListed;

// Linked into Rask.Providers.E2E.Tests as well, so the move to HasMany is compared on every provider. The same names as
// Shapes.Listed, in a namespace of their own: the table, the navigation and the foreign key are named after them.

/// <summary>An aggregate's children as an app writes them by hand: a private list and a read-only view of it.</summary>
public sealed class Rack
{
    private readonly List<Shelf> _shelves = [];

    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public IReadOnlyList<Shelf> Shelves => _shelves;
}

/// <summary>A rack's shelf: a child, by the IEntity it is.</summary>
public sealed class Shelf : IEntity
{
    public Guid Id { get; set; }

    public int Slots { get; set; }
}

/// <summary>The rack and its shelves under Rask's conventions.</summary>
public sealed class HandListedRackContext(DbContextOptions<HandListedRackContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Rack>();
        modelBuilder.ApplyRaskConventions();
    }
}
