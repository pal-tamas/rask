using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

public sealed record Enclosure(string Name, int Size);

/// <summary>A letter whose enclosures and labels are each kept as JSON in one column.</summary>
public sealed class Letter
{
    public Guid Id { get; set; }

    public List<Enclosure> Enclosures { get; set; } = [];

    public List<string> Labels { get; set; } = [];
}

/// <summary>JSON columns left to Rask's conventions, with no provider's type named.</summary>
public sealed class JsonColumnsContext(DbContextOptions<JsonColumnsContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Letter>().ComplexCollection(l => l.Enclosures, enclosures => enclosures.ToJson());
    }
}

/// <summary>On SQLite a JSON column is <c>TEXT</c>, with nothing of SQL Server's in the schema.</summary>
public sealed class JsonColumnTypeTests
{
    [Fact]
    public void A_json_column_is_text_with_no_type_named_for_it()
    {
        using var context = new JsonColumnsContext(
            OptionsLayout.LikeAddDbContext<JsonColumnsContext>().UseSqlite("Data Source=:memory:").Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Enclosures\" TEXT NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Labels\" TEXT NOT NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain("max", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_json_column_round_trips_on_SQLite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var options = OptionsLayout.LikeAddDbContext<JsonColumnsContext>().UseSqlite(connection).Options;
        await using (var write = new JsonColumnsContext(options))
        {
            await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            write.Add(new Letter { Id = Guid.NewGuid(), Enclosures = [new("invoice.pdf", 120)], Labels = ["paid"] });
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = new JsonColumnsContext(options);
        var letter = await read.Set<Letter>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new Enclosure("invoice.pdf", 120)], letter.Enclosures);
        Assert.Equal(["paid"], letter.Labels);
    }
}
