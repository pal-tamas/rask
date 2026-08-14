using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Sync.Tests;

/// <summary>A model covering the SQLite storage classes a change can carry across the wire.</summary>
public sealed class Todo
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool Done { get; set; }

    public int Priority { get; set; }

    public double Score { get; set; }

    public byte[] Attachment { get; set; } = [];

    public string? Notes { get; set; }
}

public sealed class TodoContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyCrdtConventions();
}
