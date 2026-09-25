using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An aggregate shaped the way the guide shows one — no constructor, private setters, a validation attribute, a value
// object, an optional note, a counter the application owns — so these drive the form model and the writes the source
// generator really emitted for it in this compilation, not the hook underneath them.
public sealed class Invoice : Aggregate<Guid>
{
    // Soft delete is opt-in now: this aggregate is one whose tests are about it.
    public const Deletion Deletes = Deletion.Soft;

    [Required]
    [MaxLength(40)]
    public string Title { get; private set; } = "";

    public decimal Balance { get; private set; }

    public InvoiceTotal Total { get; private set; } = new(0m, "EUR");

    public int Views { get; private set; }

    public string? Note { get; private set; }

    public static Invoice Draft(string title) => new() { Id = Guid.CreateVersion7(), Title = title };

    public void Viewed() => Views++;

    public void Retitle(string title) => Title = title;

    public void Annotate(string? note) => Note = note;
}

public sealed record InvoiceTotal(decimal Amount, string Currency);

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

    // What an edit page does: fill the form from the row it read.
    private static InvoiceModel EditOf(Invoice invoice) => invoice.ToModel();

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

        Assert.Equal(["Balance", "Note", "Title", "Total", "Version", "Views"], names);
        Assert.All(typeof(InvoiceModel).GetProperties(), p => Assert.True(p.CanWrite, p.Name));
    }

    [Fact]
    public void Every_property_of_the_model_is_nullable()
    {
        var nullability = new NullabilityInfoContext();

        Assert.All(typeof(InvoiceModel).GetProperties(), p =>
            Assert.True(
                Nullable.GetUnderlyingType(p.PropertyType) is not null ||
                nullability.Create(p).WriteState == NullabilityState.Nullable,
                p.Name));
    }

    [Fact]
    public void A_value_object_becomes_a_nested_settable_model()
    {
        var total = typeof(InvoiceModel).GetProperty(nameof(InvoiceModel.Total))!;

        Assert.Equal(typeof(InvoiceModel.InvoiceTotalModel), total.PropertyType);
        Assert.NotNull(new InvoiceModel().Total);

        var model = NewModel();
        model.Total!.Amount = 12m;
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
    public void A_new_model_holds_the_aggregates_own_defaults()
    {
        var model = new InvoiceModel();

        Assert.Equal("", model.Title);
        Assert.Equal(0m, model.Balance);
        Assert.Equal(0m, model.Total!.Amount);
        Assert.Equal("EUR", model.Total.Currency);
        Assert.Null(model.Note);
        Assert.Equal(0, model.Version);
    }

    [Fact]
    public void ToModel_copies_every_value_including_the_version_and_the_value_object()
    {
        var invoice = Invoice.Draft("March");
        invoice.Annotate("net 30");
        invoice.Viewed();

        var model = invoice.ToModel();

        Assert.Equal("March", model.Title);
        Assert.Equal("net 30", model.Note);
        Assert.Equal(1, model.Views);
        Assert.Equal("EUR", model.Total!.Currency);
        Assert.Equal(0, model.Version);
    }

    // ---- create -------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_builds_the_entity_through_its_private_members_and_generates_the_key()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Invoice.Create(NewModel());

        Assert.NotEqual(Guid.Empty, created.Id);

        var stored = await database.LoadAsync<Invoice>(created.Id);
        Assert.NotNull(stored);
        Assert.Equal("March", stored.Title);
        Assert.Equal(120.5m, stored.Balance);
        Assert.Equal(new InvoiceTotal(99m, "HUF"), stored.Total);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);
        Assert.Equal(0, stored.Views);
    }

    [Fact]
    public async Task Create_without_an_id_assigns_a_distinct_version_7_key_to_every_insert()
    {
        await using var database = await StartDatabaseAsync();

        var first = await Invoice.Create(NewModel());
        var second = await Invoice.Create(NewModel());

        Assert.NotEqual(Guid.Empty, second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(7, first.Id.Version);
        Assert.Equal(7, second.Id.Version);
    }

    [Fact]
    public async Task Create_with_an_id_inserts_the_row_under_that_id()
    {
        await using var database = await StartDatabaseAsync();
        var id = Guid.NewGuid(); // version 4: nothing generated here could have produced it

        var created = await Invoice.Create(id, NewModel());

        Assert.Equal(id, created.Id);
        Assert.Equal("March", (await database.LoadAsync<Invoice>(id))!.Title);
    }

    [Fact]
    public async Task Create_applies_values_that_do_not_come_from_the_form_after_the_model()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Invoice.Create(NewModel(), invoice =>
        {
            Assert.Equal("March", invoice.Title); // the model is already on it
            invoice.Viewed();                     // a value the form never carries
        });

        Assert.Equal(1, (await database.LoadAsync<Invoice>(created.Id))!.Views);
    }

    [Fact]
    public async Task Create_without_a_form_builds_the_row_the_lambda_describes_the_way_Update_does()
    {
        await using var database = await StartDatabaseAsync();

        var created = await Invoice.Create(invoice =>
        {
            invoice.Retitle("Walk-in");
            invoice.Viewed();
        });

        Assert.Equal(7, created.Id.Version);
        var stored = (await database.LoadAsync<Invoice>(created.Id))!;
        Assert.Equal("Walk-in", stored.Title);
        Assert.Equal(1, stored.Views);
        Assert.Equal(Start.UtcDateTime, stored.CreatedAt);

        // The same shape, one write later.
        await Invoice.Update(created.Id, invoice => invoice.Retitle("Walk-in, paid"));
        Assert.Equal("Walk-in, paid", (await database.LoadAsync<Invoice>(created.Id))!.Title);
    }

    [Fact]
    public async Task Create_without_a_form_stages_on_a_given_context_and_takes_a_given_id()
    {
        await using var database = await StartDatabaseAsync();
        var id = Guid.NewGuid();

        var created = await Invoice.Create(id, invoice => invoice.Retitle("Keyed"), db: database.Context);

        // The caller chose the key, so it is already there — but the row is not, because `db:` stages.
        Assert.Equal(id, created.Id);
        Assert.Equal(EntityState.Added, database.Context.Entry(created).State);
        Assert.Null(await database.LoadAsync<Invoice>(id));

        await database.Context.SaveChangesAsync();
        Assert.Equal("Keyed", (await database.LoadAsync<Invoice>(id))!.Title);
    }

    [Fact]
    public async Task Create_of_a_built_entity_inserts_it_as_it_was_built()
    {
        await using var database = await StartDatabaseAsync();
        var draft = Invoice.Draft("Built");

        var created = await Invoice.Create(draft);

        Assert.Same(draft, created);
        Assert.Equal("Built", (await database.LoadAsync<Invoice>(created.Id))!.Title);
        Assert.Equal(Start.UtcDateTime, created.CreatedAt);
    }

    // ---- update -------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_writes_the_edit_and_bumps_the_version()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());
        var edit = EditOf((await database.LoadAsync<Invoice>(created.Id))!);

        edit.Title = "April";
        edit.Total!.Amount = 150m;
        var updated = await Invoice.Update(created.Id, edit);

        var stored = await database.LoadAsync<Invoice>(created.Id);
        Assert.Equal("April", stored!.Title);
        Assert.Equal(new InvoiceTotal(150m, "HUF"), stored.Total);
        Assert.Equal(1, stored.Version);
        Assert.Equal(stored.Version, updated.Version);
    }

    [Fact]
    public async Task A_null_clears_a_nullable_property_and_leaves_a_non_nullable_one()
    {
        await using var database = await StartDatabaseAsync();
        var model = NewModel();
        model.Note = "net 30";
        var created = await Invoice.Create(model);

        // The form emptied Note, and never set Balance or Total at all.
        var edit = new InvoiceModel(blank: true) { Title = "April", Note = null, Version = created.Version };
        await Invoice.Update(created.Id, edit);

        var stored = (await database.LoadAsync<Invoice>(created.Id))!;
        Assert.Equal("April", stored.Title);
        Assert.Null(stored.Note);
        Assert.Equal(120.5m, stored.Balance);
        Assert.Equal(new InvoiceTotal(99m, "HUF"), stored.Total);
    }

    [Fact]
    public async Task Update_with_a_stale_version_throws_and_leaves_the_row_alone()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());

        var first = EditOf((await database.LoadAsync<Invoice>(created.Id))!);
        var second = EditOf((await database.LoadAsync<Invoice>(created.Id))!);

        first.Title = "First";
        await Invoice.Update(created.Id, first);

        second.Title = "Second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.Update(created.Id, second));

        Assert.Equal("First", (await database.LoadAsync<Invoice>(created.Id))!.Title);
    }

    [Fact]
    public async Task Update_applies_values_that_do_not_come_from_the_form_after_the_model()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());
        var edit = EditOf(created);
        edit.Title = "April";

        await Invoice.Update(created.Id, edit, invoice => invoice.Viewed());

        var stored = (await database.LoadAsync<Invoice>(created.Id))!;
        Assert.Equal("April", stored.Title);
        Assert.Equal(1, stored.Views);
    }

    [Fact]
    public async Task Update_without_a_form_applies_the_change_and_checks_a_version_only_when_given_one()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());

        var updated = await Invoice.Update(created.Id, invoice => invoice.Viewed());        // version 0 -> 1
        Assert.Equal(1, updated.Views);
        Assert.Equal(1, updated.Version);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Invoice.Update(created.Id, invoice => invoice.Viewed(), version: 0));
        Assert.Equal(1, (await database.LoadAsync<Invoice>(created.Id))!.Views);
    }

    [Fact]
    public async Task Update_for_a_missing_row_is_KeyNotFound()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoice.Update(Guid.NewGuid(), NewModel()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Invoice.Update(Guid.NewGuid(), i => i.Viewed()));
    }

    // ---- delete -------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_checks_the_version_when_given_one_and_deletes_by_id_when_not()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());
        // A real edit: an update that changes nothing writes nothing, and would leave the version at 0.
        var edit = EditOf(created);
        edit.Title = "Bumped";
        await Invoice.Update(created.Id, edit);         // version 0 -> 1

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Invoice.Delete(created.Id, version: 0));
        Assert.NotNull(await database.LoadAsync<Invoice>(created.Id));

        await Invoice.Delete(created.Id);
        Assert.Null(await database.LoadAsync<Invoice>(created.Id));
    }

    // ---- the form loop's fill -------------------------------------------------------------------------

    [Fact]
    public async Task Model_fills_the_edit_shape_by_id()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());

        var model = await Invoice.Model(created.Id);

        Assert.NotNull(model);
        Assert.Equal("March", model!.Title);

        // Nested, not flattened: this is the FORM's shape, which is why it is not a read-face query.
        Assert.Equal(99m, model.Total!.Amount);
        Assert.Equal("HUF", model.Total.Currency);

        Assert.Null(await Invoice.Model(Guid.NewGuid()));
    }

    [Fact]
    public async Task Model_does_not_open_a_form_on_a_deleted_row()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());
        await Invoice.Delete(created.Id);

        // EF Core's Find would hand this back — it skips query filters. Model is a query, so it does not.
        Assert.Null(await Invoice.Model(created.Id));
    }

    // ---- named sets ---------------------------------------------------------------------------------

    [Fact]
    public async Task An_entity_is_reachable_by_name_on_the_context()
    {
        await using var database = await StartDatabaseAsync();
        var created = await Invoice.Create(NewModel());

        // db.Invoices is an extension member on DbContext itself — so a context Rask never declared has it,
        // and it is the very set Set<T>() returns rather than a second one built beside it.
        var db = database.Context;
        Assert.Same(db.Set<Invoice>(), db.Invoices);
        Assert.Equal(created.Id, (await db.Invoices.SingleAsync()).Id);
    }

    // ---- a caller's context -------------------------------------------------------------------------

    [Fact]
    public async Task A_write_given_a_context_stages_and_leaves_the_save_to_the_caller()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;

        var created = await Invoice.Create(NewModel(), db: db);

        // Staged, not saved: `db:` hands the unit of work to the caller, so nothing is in the table yet.
        Assert.Equal(EntityState.Added, db.Entry(created).State);
        Assert.Null(await database.LoadAsync<Invoice>(created.Id));

        await db.SaveChangesAsync();
        Assert.Equal(EntityState.Unchanged, db.Entry(created).State);
        Assert.NotNull(await database.LoadAsync<Invoice>(created.Id));

        // A row that context already tracks is the one updated, not a second copy — and it is staged too.
        var updated = await Invoice.Update(created.Id, invoice => invoice.Viewed(), db: db);
        Assert.Same(created, updated);
        Assert.Equal(0, (await database.LoadAsync<Invoice>(created.Id))!.Views);

        await db.SaveChangesAsync();
        Assert.Equal(1, (await database.LoadAsync<Invoice>(created.Id))!.Views);

        // And the delete, which is a soft delete, is no different: stamped in the tracker, written on save.
        await Invoice.Delete(created.Id, db: db);
        Assert.NotNull(await database.LoadAsync<Invoice>(created.Id));

        await db.SaveChangesAsync();
        Assert.Null(await database.LoadAsync<Invoice>(created.Id));
    }

    [Fact]
    public async Task Two_writes_staged_on_one_context_commit_under_one_save()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;
        var kept = await Invoice.Create(NewModel());

        // Two aggregates changed together, with no BeginTransactionAsync anywhere: one SaveChangesAsync
        // already IS one transaction, which is the whole point of `db:` no longer saving on its own.
        var placed = await Invoice.Create(NewModel(), db: db);
        await Invoice.Update(kept.Id, invoice => invoice.Viewed(), db: db);

        Assert.Null(await database.LoadAsync<Invoice>(placed.Id));
        Assert.Equal(0, (await database.LoadAsync<Invoice>(kept.Id))!.Views);

        await db.SaveChangesAsync();

        Assert.NotNull(await database.LoadAsync<Invoice>(placed.Id));
        Assert.Equal(1, (await database.LoadAsync<Invoice>(kept.Id))!.Views);
    }

    [Fact]
    public async Task A_save_that_fails_takes_every_write_staged_beside_it_with_it()
    {
        await using var database = await StartDatabaseAsync();
        var db = database.Context;
        var taken = await Invoice.Create(NewModel());
        var kept = await Invoice.Create(NewModel());

        // An insert the database will refuse — that key is already a row — staged beside an update that
        // would have succeeded on its own.
        await Invoice.Create(taken.Id, NewModel(), db: db);
        await Invoice.Update(kept.Id, invoice => invoice.Viewed(), db: db);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // Neither landed. Before `db:` staged, the update was its own transaction and would have survived.
        db.ChangeTracker.Clear();
        Assert.Equal(0, (await database.LoadAsync<Invoice>(kept.Id))!.Views);
    }
}
