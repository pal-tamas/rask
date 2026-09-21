using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// Models for JsonIndexTests. EF caches a built model per context type, so every variant is its own type.
internal sealed class JsonOrder
{
    public int Id { get; set; }

    public string Note { get; set; } = "";

    public OrderMeta Meta { get; set; } = new();
}

internal sealed class OrderMeta
{
    public string Status { get; set; } = "";

    public OrderAddress Address { get; set; } = new();
}

internal sealed class OrderAddress
{
    public string City { get; set; } = "";
}

internal class UnindexedOrderContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<JsonOrder> Orders => Set<JsonOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<JsonOrder>().OwnsOne(o => o.Meta, meta =>
        {
            meta.ToJson();
            meta.OwnsOne(m => m.Address);
        });
}

internal class IndexedOrderContext(DbContextOptions options) : UnindexedOrderContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<JsonOrder>()
            .HasJsonIndex(o => o.Meta.Status)
            .HasJsonIndex(o => o.Meta.Address.City);
    }
}

// Same indexes, and Note becomes required — a change SQLite cannot apply in place, so EF rebuilds the table.
internal sealed class RebuiltIndexedOrderContext(DbContextOptions options) : IndexedOrderContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<JsonOrder>().Property(o => o.Note).HasMaxLength(200).HasDefaultValue("");
    }
}

internal sealed class NotJsonContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<JsonOrder>(order =>
        {
            order.OwnsOne(o => o.Meta, meta => meta.OwnsOne(m => m.Address));
            order.HasJsonIndex(o => o.Meta.Status);
        });
}
