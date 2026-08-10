using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Tests;

/// <summary>
///     A model covering every CLR shape the convention has to map, plus the three it must leave alone:
///     a key, a nullable, and a column that already carries a default.
/// </summary>
public sealed class Todo
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool Done { get; set; }

    public int Priority { get; set; }

    public double Score { get; set; }

    public decimal Cost { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public TimeSpan Estimate { get; set; }

    public Guid OwnerId { get; set; }

    public byte[] Attachment { get; set; } = [];

    public TodoState State { get; set; }

    /// <summary>Nullable: cr-sqlite is happy with it, so the convention must not touch it.</summary>
    public string? Notes { get; set; }

    /// <summary>Carries an explicit default from <c>OnModelCreating</c>, which must survive.</summary>
    public string Slug { get; set; } = string.Empty;
}

public enum TodoState
{
    Open = 0,
    Closed = 1,
}

public class TodoContext(DbContextOptions options) : DbContext(options)
{
    public const string ExplicitSlugDefault = "'untitled'";

    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>().Property(t => t.Slug).HasDefaultValueSql(ExplicitSlugDefault);
        modelBuilder.ApplyCrdtConventions();
    }
}

/// <summary>The same model without the convention, to show what EF produces on its own.</summary>
public sealed class PlainTodoContext(DbContextOptions options) : TodoContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>().Property(t => t.Slug).HasDefaultValueSql(ExplicitSlugDefault);
    }
}
