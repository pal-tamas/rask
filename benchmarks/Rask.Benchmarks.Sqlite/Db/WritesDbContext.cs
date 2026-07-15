using Microsoft.EntityFrameworkCore;

namespace Rask.Benchmarks.Sqlite.Db;

/// <summary>The EF arms' model, mapped onto the same <c>writes</c> table the raw arms use.</summary>
internal sealed class WritesDbContext(DbContextOptions<WritesDbContext> options) : DbContext(options)
{
    internal DbSet<WriteRow> Writes => Set<WriteRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<WriteRow>();
        row.ToTable("writes");
        row.Property(r => r.Id).HasColumnName("id");
        row.Property(r => r.Worker).HasColumnName("worker");
        row.Property(r => r.Payload).HasColumnName("payload");
    }
}

internal sealed class WriteRow
{
    public int Id { get; set; }

    public int Worker { get; set; }

    public string Payload { get; set; } = string.Empty;
}
