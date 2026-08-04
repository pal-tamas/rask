using Microsoft.EntityFrameworkCore;
using Rask.Data;
using Rask.Outbox;
using Rask.Jobs;
using Rask.Mail;
using Rask.Cache;
using Rask.Example.Shop.Features.Products;
using Rask.Example.Shop.Features.Orders;

namespace Rask.Example.Shop.Features.Shared;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ApplyRaskConventions walks the model as it stands, applying the soft-delete query filter and
        // the concurrency token to whatever is already in it — so it has to follow the configurations,
        // not precede them, or entities registered afterwards silently miss out.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyRaskConventions();
        modelBuilder.AddRaskOutbox();
        modelBuilder.AddRaskJobs();
        modelBuilder.AddRaskMail();
        modelBuilder.AddRaskCache();
    }
}
