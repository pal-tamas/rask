using Microsoft.EntityFrameworkCore;
using Rask.Mail;

namespace Rask.Example.EfCore.Features.Catalog.Shared;

// The Catalog bounded context's DbContext. Resolved through IDbContextFactory (see Program.cs),
// so every slice gets a fresh short-lived instance per operation rather than a long-lived one
// pinned to the WebSocket session.
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder
            .ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly)
            .AddRaskMail();   // maps the QueuedMail table onto this context (EnsureCreated builds it, see CatalogSeeder)
}
