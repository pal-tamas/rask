using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Mirrors exactly what `rask new --data` writes into Features/Shared/AppDbContext.cs: a context over
// plain DbContext whose OnModelCreating applies configurations, the battery tables and the Rask
// conventions. Nothing here reaches ModelRegistry, which is the point of the test.
internal sealed class ScaffoldShapedContext(DbContextOptions<ScaffoldShapedContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScaffoldShapedContext).Assembly);
        modelBuilder.ApplyRaskConventions();
    }
}

// The same shape, but over RaskDbContext — the one-word change under test.
internal sealed class RaskShapedContext(DbContextOptions<RaskShapedContext> options)
    : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaskShapedContext).Assembly);
        modelBuilder.ApplyRaskConventions();
    }
}

// Collected like every other class here: both cases below build a context, and EF's model cache is per
// process, so running in parallel with another such class drives one IModel from two threads.
[Collection(DataDbCollection.Name)]
public sealed class ScaffoldContextShapeTests
{
    [Fact]
    public void A_context_over_plain_DbContext_does_not_map_a_declared_model()
    {
        using var db = new ScaffoldShapedContext(
            new DbContextOptionsBuilder<ScaffoldShapedContext>().UseSqlite("Data Source=:memory:").Options);

        Assert.Null(db.Model.FindEntityType(typeof(Doodad)));
    }

    [Fact]
    public void A_context_over_RaskDbContext_maps_it()
    {
        using var db = new RaskShapedContext(
            new DbContextOptionsBuilder<RaskShapedContext>().UseSqlite("Data Source=:memory:").Options);

        Assert.NotNull(db.Model.FindEntityType(typeof(Doodad)));
    }
}
