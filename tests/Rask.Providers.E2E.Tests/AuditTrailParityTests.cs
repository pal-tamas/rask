using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A delivery note whose every save is audited: an identity key, a decimal, and legs of its own.</summary>
public sealed class AuditedNote : Aggregate<int, Versioned, Audited>
{
    private readonly List<AuditedNoteLeg> _legs = [];

    private AuditedNote() { } // EF materialization

    public string Number { get; private set; } = "";

    public int? CityKm { get; private set; }

    public decimal Weight { get; private set; }

    public IReadOnlyCollection<AuditedNoteLeg> Legs => _legs;

    public static AuditedNote Write(string number, decimal weight, decimal litres)
    {
        var note = new AuditedNote { Number = number, CityKm = 40, Weight = weight };
        note._legs.Add(AuditedNoteLeg.Of(litres));
        return note;
    }

    public void Change(int? cityKm, decimal litres)
    {
        CityKm = cityKm;
        _legs.First().Refill(litres);
    }
}

public sealed class AuditedNoteLeg : Entity<int>
{
    private AuditedNoteLeg() { } // EF materialization

    public decimal Litres { get; private set; }

    public void Refill(decimal litres) => Litres = litres;

    internal static AuditedNoteLeg Of(decimal litres) => new() { Litres = litres };
}

public sealed class AuditedNoteDbContext(DbContextOptions<AuditedNoteDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_audit_trail";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<AuditedNote>().Property(n => n.Weight).HasPrecision(18, 3);
        modelBuilder.Entity<AuditedNoteLeg>().Property(l => l.Litres).HasPrecision(18, 2);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     <c>Audited</c> on every provider: the entry is written in the save — the identity key the database generated read
///     into it before the commit — with its children's rows under their place, in the invariant culture. Rask writes no
///     provider SQL for it, so it runs on SQLite alone.
/// </summary>
public abstract class AuditTrailParityTests(ProviderLeg leg) : ParityTests<AuditedNoteDbContext>(leg)
{
    [Fact]
    public async Task An_insert_leaves_its_entry_with_the_generated_keys()
    {
        RequireServer();
        await using var db = Open();
        var note = AuditedNote.Write("W-1", 1.5m, 12.25m);

        db.Add(note);
        await db.SaveChangesAsync(Ct);

        var entry = await EntryAsync();
        Assert.Equal((AuditAction.Insert, $"Id={note.Id}"), (entry.Action, entry.Key));
        Assert.Contains($"Id={note.Id} || CityKm=40 || Number=W-1", entry.After, StringComparison.Ordinal);
        Assert.Contains("Weight=1.5", entry.After, StringComparison.Ordinal);
        Assert.Contains($"Legs[0].Id={note.Legs.Single().Id}", entry.After, StringComparison.Ordinal);
        Assert.Contains("Legs[0].Litres=12.25", entry.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_records_what_changed_on_the_root_and_its_rows()
    {
        RequireServer();
        var id = await NoteAsync();
        await using var db = Open();
        var note = await db.Set<AuditedNote>().Include(n => n.Legs).SingleAsync(n => n.Id == id, Ct);

        note.Change(cityKm: null, litres: 20m);
        await db.SaveChangesAsync(Ct);

        var entry = await EntryAsync();
        Assert.Equal(AuditAction.Update, entry.Action);
        Assert.Equal("CityKm=40 || Version=0 || Legs[0].Litres=12.25", entry.Before);
        Assert.Equal("Version=1 || Legs[0].Litres=20", entry.After);
    }

    private protected override AuditedNoteDbContext Open() => Leg.Open<AuditedNoteDbContext>(o => new AuditedNoteDbContext(o));

    private async Task<int> NoteAsync()
    {
        await using var db = Open();
        var note = AuditedNote.Write("W-1", 1.5m, 12.25m);
        db.Add(note);
        await db.SaveChangesAsync(Ct);
        return note.Id;
    }

    private async Task<AuditEntry> EntryAsync()
    {
        await using var db = Open();
        return await db.Set<AuditEntry>().OrderByDescending(e => e.Id).FirstAsync(Ct);
    }
}

public sealed class SqliteAuditTrailParityTests() : AuditTrailParityTests(ProviderLeg.Sqlite("audit-trail"));

