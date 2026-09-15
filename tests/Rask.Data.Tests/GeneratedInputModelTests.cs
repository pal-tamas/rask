using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An entity shaped the way the guide shows one — private constructor, private setters, a validation attribute,
// a value object, a counter the application owns — so these drive the form model and the writes the source
// generator really emitted for it in this compilation, not the hook underneath them.
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

    public static Invoice Draft(string title) => new() { Id = Guid.CreateVersion7(), Title = title };

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

    // What an edit page does by hand: fill the form from the row it read.
    private static InvoiceModel EditOf(Invoice invoice) => new()
    {
        Title = invoice.Title,
        Balance = invoice.Balance,
        Total = new InvoiceModel.InvoiceTotalModel { Amount = invoice.Total.Amount, Currency = invoice.Total.Currency },
        Version = invoice.Version,
    };

    private static bool IsValid(object model, out List<ValidationResult> results)
    {
        results = [];
        return Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
    }

    // ---- the model ---------------------------------------------------------------------------------

    [Fact]
    public void The_model_carries_the_mapped_values_and_the_version_but_no_id_and_no_framework_columns()
    {
        var names = typeof(InvoiceModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Balance", "Title", "Total", "Version"], names);
        Assert.All(typeof(InvoiceModel).GetProperties(), p => Assert.True(p.CanWrite, p.Name));
    }

    [Fact]
    public void A_skipped_property_is_left_off_the_model()
    {
        Assert.Null(typeof(InvoiceModel).GetProperty(nameof(Invoice.Views)));
    }

    [Fact]
    public void A_value_object_becomes_a_nested_settable_model()
    {
        var total = typeof(InvoiceModel).GetProperty(nameof(InvoiceModel.Total))!;

        Assert.Equal(typeof(InvoiceModel.InvoiceTotalModel), total.PropertyType);
        Assert.NotNull(new InvoiceModel().Total);

        var model = NewModel();
        model.Total.Amount = 12m;
        Assert.Equal(12m, model.Total.Amount);
    }

    [Fact]
    public void The_entity_validation_attributes_are_checked_on_the_model()
    {
        Assert.True(IsValid(NewModel(), out _));

        var blank = NewModel();
        blank.Title = "";
        Assert.False(IsValid(blank, out var missing));
        Assert.Contains(missing, r => r.MemberNames.Contains(nameof(InvoiceModel.Title)));

        var tooLong = NewModel();
        tooLong.Title = new string('x', 41);
        Assert.False(IsValid(tooLong, out var overflow));
        Assert.Contains(overflow, r => r.MemberNames.Contains(nameof(InvoiceModel.Title)));
    }

    [Fact]
    public void The_model_carries_only_its_values_and_there_is_no_ToModel()
    {
        Assert.DoesNotContain(
            typeof(InvoiceModel).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            m => !m.IsSpecialName);
        Assert.DoesNotContain(typeof(InvoiceModelExtensions).GetMethods(), m => m.Name == "ToModel");
    }

    // ---- create -------------------------------------------------------------------------------------

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
    public async Task CreateAsync_applies_values_that_do_not_come_from_the_form_after_the_model()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Invoice.CreateAsync(NewModel(), invoice =>
        {
            Assert.Equal("March", invoice.Title); // the model is already on it
            invoice.Viewed();                     // a [SkipModel] value the form never carries
        });

        Assert.Equal(1, (await Invoice.FindAsync(created.Id))!.Views);
    }

    [Fact]
    public async Task CreateAsync_of_a_built_entity_inserts_it_as_it_was_built()
    {
        await using var database = await StartDatabaseAsync();
        var draft = Invoice.Draft("Built");

        var created = await Invoice.CreateAsync(draft);

        Assert.Same(draft, created);
        Assert.Equal("Built", (await Invoice.FindAsync(created.Id))!.Title);
        Assert.Equal(Start.UtcDateTime, created.CreatedAt);
    }

    // ---- update -------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_writes_the_edit_bumps_the_version_and_never_touches_a_skipped_property()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());
        var edit = EditOf((await Invoice.FindAsync(created.Id))!);

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

        var first = EditOf((await Invoice.FindAsync(created.Id))!);
        var second = EditOf((await Invoice.FindAsync(created.Id))!);

        first.Title = "First";
        await Invoice.UpdateAsync(created.Id, first);

        second.Title = "Second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.UpdateAsync(created.Id, second));

        Assert.Equal("First", (await Invoice.FindAsync(created.Id))!.Title);
    }

    [Fact]
    public async Task UpdateAsync_applies_values_that_do_not_come_from_the_form_after_the_model()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());
        var edit = EditOf(created);
        edit.Title = "April";

        await Invoice.UpdateAsync(created.Id, edit, invoice => invoice.Viewed());

        var stored = (await Invoice.FindAsync(created.Id))!;
        Assert.Equal("April", stored.Title);
        Assert.Equal(1, stored.Views);
    }

    [Fact]
    public async Task UpdateAsync_without_a_form_applies_the_change_and_checks_a_version_only_when_given_one()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());

        var updated = await Invoice.UpdateAsync(created.Id, invoice => invoice.Viewed());   // version 0 -> 1
        Assert.Equal(1, updated.Views);
        Assert.Equal(1, updated.Version);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Invoice.UpdateAsync(created.Id, invoice => invoice.Viewed(), version: 0));
        Assert.Equal(1, (await Invoice.FindAsync(created.Id))!.Views);
    }

    [Fact]
    public async Task UpdateAsync_for_a_missing_row_is_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoice.UpdateAsync(Guid.NewGuid(), NewModel()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoice.UpdateAsync(Guid.NewGuid(), i => i.Viewed()));
    }

    // ---- delete -------------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_checks_the_version_when_given_one_and_deletes_by_id_when_not()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.CreateAsync(NewModel());
        // A real edit: an update that changes nothing writes nothing, and would leave the version at 0.
        var edit = EditOf(created);
        edit.Title = "Bumped";
        await Invoice.UpdateAsync(created.Id, edit);    // version 0 -> 1

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.DeleteAsync(created.Id, version: 0));
        Assert.NotNull(await Invoice.FindAsync(created.Id));

        await Invoice.DeleteAsync(created.Id);
        Assert.Null(await Invoice.FindAsync(created.Id));
    }

    // ---- a caller's context -------------------------------------------------------------------------

    [Fact]
    public async Task A_write_given_a_context_saves_through_it_and_leaves_it_open()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;

        var created = await Invoice.CreateAsync(NewModel(), db: db);

        // Tracked by the caller's context — the write happened there — and the context is still usable.
        Assert.Equal(EntityState.Unchanged, db.Entry(created).State);
        Assert.Equal(1, await db.Set<Invoice>().CountAsync());

        // A row that context already tracks is the one updated, not a second copy.
        var updated = await Invoice.UpdateAsync(created.Id, invoice => invoice.Viewed(), db: db);
        Assert.Same(created, updated);
        Assert.Equal(1, (await Invoice.FindAsync(created.Id))!.Views);

        await Invoice.DeleteAsync(created.Id, db: db);
        Assert.Null(await Invoice.FindAsync(created.Id));
        Assert.Equal(0, await db.Set<Invoice>().CountAsync());
    }

    [Fact]
    public async Task Writes_given_a_context_join_its_transaction_and_roll_back_with_it()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;
        var kept = await Invoice.CreateAsync(NewModel());

        Guid discarded;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            discarded = (await Invoice.CreateAsync(NewModel(), db: db)).Id;
            await Invoice.UpdateAsync(kept.Id, invoice => invoice.Viewed(), db: db);
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.Null(await Invoice.FindAsync(discarded));
        Assert.Equal(0, (await Invoice.FindAsync(kept.Id))!.Views);
    }
}
