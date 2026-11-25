using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;
using Rask.Data;

namespace Rask.Storage.Tests;

public sealed class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Plate { get; set; } = "";

    public Attachment? Document { get; set; }

    public Papers? Papers { get; set; }

    public List<Attachment> Extras { get; set; } = [];

    public List<Tyre> Tyres { get; set; } = [];
}

/// <summary>A value object that holds a file.</summary>
public sealed record Papers
{
    private Papers() { }

    public Papers(string note, Attachment? scan) => (Note, Scan) = (note, scan);

    public string Note { get; private init; } = "";

    public Attachment? Scan { get; private init; }
}

/// <summary>A child row that holds a file.</summary>
public sealed class Tyre
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid VehicleId { get; set; }

    public Attachment? Photo { get; set; }
}

/// <summary>The storage table beside a table whose rows hold an attachment, with the interceptors a host hands it.</summary>
public sealed class AttachmentDbContext(DbContextOptions<AttachmentDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(new AttachmentInterceptor());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddRaskStorage();
        modelBuilder.Entity<Vehicle>(b =>
        {
            b.ComplexProperty(v => v.Document);
            b.ComplexProperty(v => v.Papers, p => p.ComplexProperty(x => x.Scan));
            b.ComplexCollection(v => v.Extras, c => c.ToJson());
            b.HasMany(v => v.Tyres).WithOne().HasForeignKey(t => t.VehicleId);
        });
        modelBuilder.Entity<Tyre>(b => b.ComplexProperty(t => t.Photo));
    }
}

/// <summary>
///     An <see cref="Attachment" /> is one write with the row that holds it: stored as the row is saved, deleted once a
///     save replaces, clears or deletes it, and gone again when the save fails or rolls back.
/// </summary>
public sealed class AttachmentTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Attachment Scan(string name = "scan.png") => Attachment.Of(Files.Save(new MemoryStream(Samples.Png()), name));

    [Fact]
    public async Task A_pending_attachment_is_stored_as_its_row_is_saved()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        var vehicle = new Vehicle { Plate = "ABC-123", Document = Scan() };

        await using (var db = harness.NewContext())
        {
            db.Add(vehicle);
            await db.SaveChangesAsync(Cancel);
        }

        await using var read = harness.NewContext();
        var stored = await read.Set<Vehicle>().SingleAsync(Cancel);
        Assert.False(stored.Document!.IsPending);
        Assert.Equal("scan.png", stored.Document.Name);
        Assert.Equal("image/png", stored.Document.ContentType);
        Assert.Equal(256, stored.Document.Size);
        Assert.NotNull(await harness.Files.Get(stored.Document.Id, Cancel));
        Assert.Single(harness.StoredPaths());
    }

    [Fact]
    public async Task A_replaced_attachment_has_its_old_file_deleted_once_the_save_goes_through()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Plate = "ABC-123", Document = Scan("old.png") });
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = harness.NewContext())
        {
            var vehicle = await db.Set<Vehicle>().SingleAsync(Cancel);
            vehicle.Document = Scan("new.png");
            await db.SaveChangesAsync(Cancel);
        }

        await using var read = harness.NewContext();
        Assert.Equal("new.png", (await read.Set<Vehicle>().SingleAsync(Cancel)).Document!.Name);
        Assert.Equal("new.png", Assert.Single(await read.Set<StoredFile>().ToListAsync(Cancel)).Name);
        Assert.Single(harness.StoredPaths());
    }

    [Fact]
    public async Task A_cleared_attachment_and_a_deleted_row_let_their_files_go()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        await using (var db = harness.NewContext())
        {
            db.AddRange(new Vehicle { Plate = "CLEARED", Document = Scan() }, new Vehicle { Plate = "DELETED", Document = Scan() });
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = harness.NewContext())
        {
            var vehicles = await db.Set<Vehicle>().ToListAsync(Cancel);
            vehicles.Single(v => v.Plate == "CLEARED").Document = null;
            db.Remove(vehicles.Single(v => v.Plate == "DELETED"));
            await db.SaveChangesAsync(Cancel);
        }

        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
    }

    [Fact]
    public async Task A_save_that_fails_outside_a_transaction_keeps_no_file_it_stored()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        var id = Guid.NewGuid();
        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Id = id, Plate = "FIRST" });
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Id = id, Plate = "TWICE", Document = Scan() });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Cancel));
        }

        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
    }

    [Fact]
    public async Task An_attachment_saved_in_a_rolled_back_transaction_leaves_no_row_and_no_bytes()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        using var facade = Db.Use(harness.NewContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            // Joined as an app's context is by Rask.Data's interceptor, which this bare context does not carry.
            await using var db = AmbientTransaction.Join(harness.NewContext());
            db.Add(new Vehicle { Plate = "ABC-123", Document = Scan() });
            await db.SaveChangesAsync(Cancel);
            throw new InvalidOperationException("the registration was refused");
        }, cancellationToken: Cancel));

        await using var read = harness.NewContext();
        Assert.Empty(await read.Set<Vehicle>().ToListAsync(Cancel));
        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
    }

    [Fact]
    public async Task A_file_the_store_refuses_is_a_validation_failure_on_the_property()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>(o => o.MaxFileSize = 16);
        using var work = Ambient.Enter(harness.Services);

        await using var db = harness.NewContext();
        db.Add(new Vehicle { Plate = "ABC-123", Document = Scan() });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Cancel));

        Assert.Equal(["Document"], Assert.Single(refused.Failures).Fields);
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_pending_attachment_saved_synchronously_is_refused_before_anything_is_written()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);

        await using var db = harness.NewContext();
        db.Add(new Vehicle { Plate = "ABC-123", Document = Scan() });
        var refused = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains("SaveChangesAsync", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task An_attachment_inside_a_value_object_is_stored_and_let_go_when_replaced()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Plate = "ABC-123", Papers = new Papers("first", Scan("first.png")) });
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = harness.NewContext())
        {
            var vehicle = await db.Set<Vehicle>().SingleAsync(Cancel);
            vehicle.Papers = new Papers("second", Scan("second.png"));
            await db.SaveChangesAsync(Cancel);
        }

        await using var read = harness.NewContext();
        var stored = await read.Set<Vehicle>().SingleAsync(Cancel);
        Assert.Equal("second.png", stored.Papers!.Scan!.Name);
        Assert.Equal("second.png", Assert.Single(await read.Set<StoredFile>().ToListAsync(Cancel)).Name);
    }

    [Fact]
    public async Task Attachments_in_a_json_list_are_stored_and_the_one_taken_out_is_let_go()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Plate = "ABC-123", Extras = [Scan("a.png"), Scan("b.png")] });
            await db.SaveChangesAsync(Cancel);
        }

        await using (var db = harness.NewContext())
        {
            var vehicle = await db.Set<Vehicle>().SingleAsync(Cancel);
            vehicle.Extras = [vehicle.Extras[1], Scan("c.png")];
            await db.SaveChangesAsync(Cancel);
        }

        await using var read = harness.NewContext();
        Assert.Equal(["b.png", "c.png"], (await read.Set<Vehicle>().SingleAsync(Cancel)).Extras.Select(e => e.Name));
        Assert.Equal(["b.png", "c.png"], (await read.Set<StoredFile>().ToListAsync(Cancel)).Select(f => f.Name).Order());
        Assert.Equal(2, harness.StoredPaths().Length);
    }

    [Fact]
    public async Task A_child_rows_attachment_is_stored_with_it_and_let_go_when_the_row_is_removed()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>();
        using var work = Ambient.Enter(harness.Services);
        await using (var db = harness.NewContext())
        {
            db.Add(new Vehicle { Plate = "ABC-123", Tyres = [new Tyre { Photo = Scan("tyre.png") }] });
            await db.SaveChangesAsync(Cancel);
        }

        var storedBefore = await harness.CountRowsAsync();
        await using (var db = harness.NewContext())
        {
            var vehicle = await db.Set<Vehicle>().Include(v => v.Tyres).SingleAsync(Cancel);
            db.Remove(vehicle.Tyres[0]);
            await db.SaveChangesAsync(Cancel);
        }

        Assert.Equal(1, storedBefore);
        Assert.Equal(0, await harness.CountRowsAsync());
        Assert.Empty(harness.StoredPaths());
    }

    [Fact]
    public async Task A_refused_file_in_a_list_is_told_at_its_place_in_the_list()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>(o => o.MaxFileSize = 16);
        using var work = Ambient.Enter(harness.Services);

        await using var db = harness.NewContext();
        db.Add(new Vehicle { Plate = "ABC-123", Extras = [Scan()] });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Cancel));

        Assert.Equal(["Extras[0]"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task A_picked_file_is_refused_under_the_form_field_it_was_picked_in()
    {
        await using var harness = new StorageHarness<AttachmentDbContext>(o => o.MaxFileSize = 16);
        using var work = Ambient.Enter(harness.Services);

        await using var db = harness.NewContext();
        db.Add(new Vehicle { Plate = "ABC-123", Document = Attachment.Of(Files.Save(new MemoryStream(Samples.Png()), "scan.png", Cancel), "File") });
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => db.SaveChangesAsync(Cancel));

        Assert.Equal(["File"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public void Storage_hands_every_host_context_the_attachment_interceptor()
    {
        var services = new ServiceCollection().AddRaskStorage<AttachmentDbContext>();

        var registered = services.Where(d => d.ServiceType == typeof(ISaveChangesInterceptor)).Select(d => d.ImplementationType);

        Assert.Contains(typeof(AttachmentInterceptor), registered);
    }
}
