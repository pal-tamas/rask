using Microsoft.EntityFrameworkCore;
using Rask.SQLite.Crdt;

namespace Rask.Example.Crdt.Data;

/// <summary>A shared family todo. Ordinary EF Core — nothing here knows it is replicated.</summary>
public sealed class Todo
{
    /// <summary>
    ///     A Guid, not an autoincrementing integer: a replica identifies rows by their key across
    ///     devices, and "3" would mean a different row on every one of them.
    /// </summary>
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool Done { get; set; }

    public int Priority { get; set; }
}

public sealed class TodoDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    // The one model-side line the CRDT needs: every required column gets a SQL default, because
    // cr-sqlite refuses a NOT NULL column without one — a peer on an older schema has to be able to
    // apply a change that never mentions the column.
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyCrdtConventions();
}
