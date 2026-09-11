using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    ///     The non-generic <c>AddRaskData()</c> registers the interceptors and nothing else, so it cannot
    ///     bind the ambient database — <c>Db.Configure</c> has no context to point at.
    /// </summary>
    /// <remarks>
    ///     Mapping the models is only half of what a scaffolded app needs. Even with the context mapped,
    ///     <c>Product.Where(…)</c> goes through <c>Db</c>, and <c>Db</c> is bound by the <em>generic</em>
    ///     overload naming the context plus one <c>Db.Configure(app.Services)</c> after the container is
    ///     built. Neither is free: nothing in <c>Rask.Server</c> can do it, because it does not reference
    ///     <c>Rask.Data</c> at all.
    /// </remarks>
    [Fact]
    public void The_non_generic_AddRaskData_cannot_bind_the_ambient_database()
    {
        var services = new ServiceCollection();
        services.AddRaskData();
        services.AddDbContextFactory<RaskShapedContext>(o => o.UseSqlite("Data Source=:memory:"));

        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() => Db.Configure(provider));
        Assert.Contains("No DbContext is bound", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_generic_overload_binds_it_and_a_read_opens_that_context()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<RaskShapedContext>();
        services.AddDbContextFactory<RaskShapedContext>(o => o.UseSqlite("Data Source=:memory:"));

        using var provider = services.BuildServiceProvider();
        try
        {
            Db.Configure(provider);
            Assert.True(Db.IsConfigured);

            // Translating a query needs the bound context's model but no table, so an in-memory database
            // with no schema is enough to prove the read opened RaskShapedContext and found Doodad mapped.
            var sql = await Doodad.QueryAsync((q, _) => Task.FromResult(q.ToQueryString()));
            Assert.Contains("Doodad", sql, StringComparison.Ordinal);
        }
        finally
        {
            Db.Reset();
        }
    }
}
