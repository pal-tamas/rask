using Microsoft.EntityFrameworkCore;

namespace Rask.Outbox.Tests;

/// <summary>
///     An event's payload and what went wrong handling it are as long as they are: a plain context that bounds every
///     string it is not told otherwise about (TA's <c>HaveMaxLength(1000)</c>) leaves the outbox's text unbounded.
/// </summary>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxTableShapeTests
{
    [Theory]
    [InlineData(nameof(OutboxMessage.Payload))]
    [InlineData(nameof(OutboxMessage.Error))]
    public void An_outbox_text_stays_unbounded_under_a_plain_context_that_bounds_every_string(string column)
    {
        using var db = new BoundedStringsContext(new DbContextOptionsBuilder<BoundedStringsContext>().UseSqlite("Data Source=:memory:").Options);

        var property = db.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(column)!;

        Assert.Null(property.GetMaxLength());
    }

    [Fact]
    public void An_outbox_type_keeps_its_own_bound_under_a_plain_context_that_bounds_every_string()
    {
        using var db = new BoundedStringsContext(new DbContextOptionsBuilder<BoundedStringsContext>().UseSqlite("Data Source=:memory:").Options);

        var property = db.Model.FindEntityType(typeof(OutboxMessage))!.FindProperty(nameof(OutboxMessage.Type))!;

        Assert.Equal(512, property.GetMaxLength());
    }

    // A plain DbContext, as TA's is: every string 1000 long unless it says otherwise, and the outbox mapped by hand.
    private sealed class BoundedStringsContext(DbContextOptions<BoundedStringsContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.Properties<string>().HaveMaxLength(1000);

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskOutbox();
    }
}
