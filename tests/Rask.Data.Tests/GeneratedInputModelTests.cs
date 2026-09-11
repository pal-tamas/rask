using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An entity shaped the way the guide tells people to write one — private constructor, private setters,
// a validation attribute, a value object, a counter the application owns — so these drive the members the
// source generator really emitted for it, not the hook underneath them.
public sealed class Invoice : Model<Guid>, ITimestamped, IVersioned
{
    private Invoice() { }

    [Required]
    [MaxLength(40)]
    public string Title { get; private set; } = "";

    public decimal Balance { get; private set; }

    public InvoiceTotal Total { get; private set; } = new(0m, "EUR");

    [SkipModel]
    public int Views { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public int Version { get; private set; }

    public void Viewed() => Views++;
}

public sealed record InvoiceTotal(decimal Amount, string Currency) : IValueObject;

[Collection(DataDbCollection.Name)]
public sealed class GeneratedInputModelTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-model-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(Start);

    public void Dispose() => File.Delete(_dbPath);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);

    private static InvoiceModel NewModel() => new()
    {
        Title = "March",
        Balance = 120.5m,
        Total = new InvoiceModel.InvoiceTotalModel { Amount = 99m, Currency = "HUF" },
    };

    [Fact]
    public async Task CreateAsync_builds_the_entity_through_its_private_members_and_generates_the_key()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Invoice.CreateAsync(NewModel());

        Assert.NotEqual(Guid.Empty, created.Id);

        var stored = await Invoice.FindAsync(created.Id);
        Assert.NotNull(stored);
        Assert.Equal("March", stored.Title);
        Assert.Equal(120.5m, stored.Balance);
        Assert.Equal(new InvoiceTotal(99m, "HUF"), stored.Total);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
        Assert.Equal(0, stored.Views);
    }

    [Fact]
    public async Task CreateAsync_without_an_id_assigns_a_distinct_version_7_key_to_every_insert()
    {
        await using var database = await StartDatabaseAsync();

        var first = await Invoice.CreateAsync(NewModel());
        var second = await Invoice.CreateAsync(NewModel());

        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(7, first.Id.Version);
        Assert.Equal(7, second.Id.Version);
    }

    [Fact]
    public async Task CreateAsync_with_an_id_inserts_the_row_under_that_id()
    {
        await using var database = await StartDatabaseAsync();
        var id = Guid.NewGuid(); // version 4: nothing generated here could have produced it

        var created = await Invoice.CreateAsync(id, NewModel());

        Assert.Equal(id, created.Id);
        Assert.Equal("March", (await Invoice.FindAsync(id))!.Title);
    }

    [Fact]
    public async Task ToModel_copies_every_carried_value_including_the_version_and_the_value_object()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());

        var model = (await Invoice.FindAsync(created.Id))!.ToModel();

        Assert.Equal("March", model.Title);
        Assert.Equal(120.5m, model.Balance);
        Assert.Equal(99m, model.Total.Amount);
        Assert.Equal("HUF", model.Total.Currency);
        Assert.Equal(0, model.Version);
    }

    [Fact]
    public async Task UpdateAsync_writes_the_edit_bumps_the_version_and_never_touches_a_skipped_property()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());
        var edit = (await Invoice.FindAsync(created.Id))!.ToModel();

        // The application moves the skipped counter on its own, between the read and the save.
        var tracked = await database.Context.Set<Invoice>().SingleAsync(i => i.Id == created.Id);
        tracked.Viewed();
        tracked.Viewed();
        await database.Context.SaveChangesAsync();

        edit.Version = tracked.Version;
        edit.Title = "April";
        edit.Total.Amount = 150m;
        var updated = await Invoice.UpdateAsync(created.Id, edit);

        var stored = await Invoice.FindAsync(created.Id);
        Assert.Equal("April", stored!.Title);
        Assert.Equal(new InvoiceTotal(150m, "HUF"), stored.Total);
        Assert.Equal(2, stored.Views);
        Assert.Equal(tracked.Version + 1, stored.Version);
        Assert.Equal(stored.Version, updated.Version);
    }

    [Fact]
    public async Task UpdateAsync_with_a_stale_version_throws_and_leaves_the_row_alone()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());

        var first = (await Invoice.FindAsync(created.Id))!.ToModel();
        var second = (await Invoice.FindAsync(created.Id))!.ToModel();

        first.Title = "First";
        await Invoice.UpdateAsync(created.Id, first);

        second.Title = "Second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.UpdateAsync(created.Id, second));

        Assert.Equal("First", (await Invoice.FindAsync(created.Id))!.Title);
    }

    [Fact]
    public async Task DeleteAsync_checks_the_version_when_given_one_and_deletes_by_id_when_not()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());
        // A real edit: an update that changes nothing writes nothing, and would leave the version at 0.
        var edit = created.ToModel();
        edit.Title = "Bumped";
        await Invoice.UpdateAsync(created.Id, edit);    // version 0 -> 1

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.DeleteAsync(created.Id, version: 0));
        Assert.NotNull(await Invoice.FindAsync(created.Id));

        await Invoice.DeleteAsync(created.Id);
        Assert.Null(await Invoice.FindAsync(created.Id));
    }

    [Fact]
    public async Task UpdateAsync_for_a_missing_row_is_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoice.UpdateAsync(Guid.NewGuid(), NewModel()));
    }

    [Fact]
    public void The_model_carries_the_entitys_validation_and_leaves_out_what_the_framework_or_the_app_owns()
    {
        var title = typeof(InvoiceModel).GetProperty(nameof(InvoiceModel.Title))!;
        Assert.NotNull(Attribute.GetCustomAttribute(title, typeof(RequiredAttribute)));
        Assert.Equal(40, ((MaxLengthAttribute)Attribute.GetCustomAttribute(title, typeof(MaxLengthAttribute))!).Length);

        Assert.Null(typeof(InvoiceModel).GetProperty("Views"));
        Assert.Null(typeof(InvoiceModel).GetProperty("CreatedAt"));
        Assert.NotNull(typeof(InvoiceModel).GetProperty("Version"));

        // No key: a posted form must not be able to choose the row its update writes (overposting).
        Assert.Null(typeof(InvoiceModel).GetProperty("Id"));

        var empty = new InvoiceModel();
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(empty, new ValidationContext(empty), results, true);
        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(InvoiceModel.Title)));
    }
}
