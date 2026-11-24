using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask.Data.Tests;

public enum PumpKind
{
    Manual,
    Pump,
}

/// <summary>A pump's reading, whose computed columns each say one thing the C# subset can.</summary>
public sealed class PumpReading
{
    public static readonly Expression<Func<PumpReading, int>> CountedAtIs = r => r.ModifiedPistol ?? r.Pistol;
    public static readonly Expression<Func<PumpReading, string>> NameIs = r => r.Year + "." + r.Month;
    public static readonly Expression<Func<PumpReading, string>> LabelIs = r => r.Note + "#" + r.ModifiedPistol;
    public static readonly Expression<Func<PumpReading, bool>> MovedIs = r => r.ModifiedPistol != null && r.ModifiedPistol != r.Pistol;
    public static readonly Expression<Func<PumpReading, bool>> SamePistolIs = r => r.ModifiedPistol == r.Pistol;
    public static readonly Expression<Func<PumpReading, int>> SpreadIs = r => Math.Abs(r.Pistol - r.ModifiedPistol.GetValueOrDefault()) % 7;
    public static readonly Expression<Func<PumpReading, string>> SizeIs = r => r.Litres * 2 > 100 ? "big" : r.Kind == PumpKind.Pump ? "pump" : "small";

    public Guid Id { get; set; }

    public int Pistol { get; set; }

    public int? ModifiedPistol { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public string? Note { get; set; }

    public double Litres { get; set; }

    public PumpKind Kind { get; set; }

    public int CountedAt { get; private set; }

    public string Name { get; private set; } = "";

    public string Label { get; private set; } = "";

    public bool Moved { get; private set; }

    public bool SamePistol { get; private set; }

    public int Spread { get; private set; }

    public string Size { get; private set; } = "";
}

/// <summary>A partner's grant: one live per partner, any number revoked.</summary>
public sealed class PartnerGrant
{
    public Guid Id { get; set; }

    public string Partner { get; set; } = "";

    public DateTime? RevokedAt { get; set; }

    public bool Active { get; set; }

    public PumpKind Kind { get; set; }

    public int? Level { get; set; }
}

/// <summary>A tank, whose size and creation the database defaults for rows written without them.</summary>
public sealed class StorageTank
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public int Size { get; set; }

    public PumpKind Kind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTime CountedAt { get; set; }
}

/// <summary>The three declarations, on Rask's conventions.</summary>
public sealed class NeutralContext(DbContextOptions<NeutralContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PumpReading>(b =>
        {
            b.Computed(r => r.CountedAt, PumpReading.CountedAtIs);
            b.Computed(r => r.Name, PumpReading.NameIs);
            b.Computed(r => r.Label, PumpReading.LabelIs);
            b.Computed(r => r.Moved, PumpReading.MovedIs);
            b.Computed(r => r.SamePistol, PumpReading.SamePistolIs);
            b.Computed(r => r.Spread, PumpReading.SpreadIs);
            b.Computed(r => r.Size, PumpReading.SizeIs);
        });
        modelBuilder.Entity<PartnerGrant>(b =>
        {
            b.HasIndex(g => g.Partner).IsUnique("This partner already has a live grant.").Where(g => g.RevokedAt == null);
            b.HasIndex(g => g.Kind).Where(g => g.Active && g.Kind == PumpKind.Pump);
            b.HasIndex(g => g.Level).Where(g => !g.Active && g.Level != null && g.Level != 3);
        });
        modelBuilder.Entity<StorageTank>(b =>
        {
            b.Default(t => t.Size, 1000);
            b.Default(t => t.Kind, PumpKind.Pump);
            b.Default(t => t.CreatedAt, () => Clock.Now);
            b.Default(t => t.CountedAt, () => Clock.Now.UtcDateTime);
        });
    }
}

/// <summary>A context of one declaration the subset refuses, its model built afresh for each.</summary>
public sealed class RefusedContext(DbContextOptions<RefusedContext> options, Action<ModelBuilder> declare) : RaskDbContext(options)
{
    /// <summary>On SQLite, with a model cache that never hands one context's model to the next.</summary>
    /// <remarks>
    ///     Late in a run the process has built more than twenty EF service providers (each test database is one), and
    ///     EF's warning about it would be thrown in place of the refusal these tests read.
    /// </remarks>
    public static RefusedContext Declaring(Action<ModelBuilder> declare) =>
        new(new DbContextOptionsBuilder<RefusedContext>()
            .UseSqlite("Data Source=:memory:")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
            .ReplaceService<IModelCacheKeyFactory, EachContextItsOwnModel>()
            .Options, declare);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        declare(modelBuilder);
    }
}
