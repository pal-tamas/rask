using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Postgres.Tests;

public sealed record Attachment(string Name, int Size);

/// <summary>A filing whose files are kept as JSON in one column.</summary>
public sealed class Filing
{
    public Guid Id { get; set; }

    public List<Attachment> Files { get; set; } = [];

    public List<string> Labels { get; set; } = [];
}

/// <summary>A JSON container left to Rask's conventions.</summary>
public sealed class JsonColumnsContext(DbContextOptions<JsonColumnsContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Filing>().ComplexCollection(f => f.Files, files => files.ToJson());
    }
}

/// <summary>On PostgreSQL a JSON container keeps Npgsql's own type, its native <c>jsonb</c>, and a primitive collection its array.</summary>
public sealed class JsonColumnTypeTests
{
    [Fact]
    public void A_json_container_stays_jsonb()
    {
        using var context = new JsonColumnsContext(
            new DbContextOptionsBuilder<JsonColumnsContext>().UseNpgsql("Host=unused;Database=unused").Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("files jsonb NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("labels text[] NOT NULL", script, StringComparison.Ordinal);
    }
}
