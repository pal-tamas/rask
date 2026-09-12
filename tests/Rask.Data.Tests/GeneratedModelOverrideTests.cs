using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An entity that replaces two of its generated writes. Declaring the member on the entity is the whole of
// overriding one: a type's own member wins over an extension member of the same signature, so every
// `Ticket.CreateAsync(model)` in the app reaches this code — and the generated write stays reachable
// through its extension class for an override that only wants to add to it.
public sealed class Ticket : Model<Guid>
{
    private Ticket() { }

    [Required]
    public string Title { get; private set; } = "";

    public bool Urgent { get; private set; }

    public static Task<Ticket> CreateAsync(TicketModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        model.Title = model.Title.Trim().ToUpperInvariant();
        return TicketModelExtensions.CreateAsync(model, cancellationToken);
    }

    public static Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("A ticket is closed, never deleted.");
}

[Collection(DataDbCollection.Name)]
public sealed class GeneratedModelOverrideTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-override-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    [Fact]
    public async Task A_create_the_entity_declares_replaces_the_generated_one_and_can_still_call_it()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Ticket.CreateAsync(new TicketModel { Title = "  printer jam " });

        Assert.Equal("PRINTER JAM", created.Title);
        Assert.Equal("PRINTER JAM", (await Ticket.FindAsync(created.Id))!.Title);
    }

    [Fact]
    public async Task A_delete_the_entity_declares_is_the_one_every_call_site_reaches()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Ticket.CreateAsync(new TicketModel { Title = "fax" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Ticket.DeleteAsync(created.Id));
        Assert.NotNull(await Ticket.FindAsync(created.Id));
    }

    [Fact]
    public async Task A_write_the_entity_does_not_declare_is_still_the_generated_one()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Ticket.CreateAsync(new TicketModel { Title = "scanner" });

        var edit = created.ToModel();
        edit.Urgent = true;
        await Ticket.UpdateAsync(created.Id, edit);

        Assert.True((await Ticket.FindAsync(created.Id))!.Urgent);
    }

    [Fact]
    public async Task The_generated_delete_stays_reachable_through_its_extension_class()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Ticket.CreateAsync(new TicketModel { Title = "toner" });

        await TicketModelExtensions.DeleteAsync(created.Id);

        Assert.Null(await Ticket.FindAsync(created.Id));
    }
}
