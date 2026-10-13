using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests.Holders;

// Plain types, held by no aggregate: an aggregate holding one of the refused shapes is a build error (RASK103),
// so the shapes are mapped here the way the generated registry maps them, on a context of their own.

public sealed record HeldSku(string Value);

public sealed record HeldLabel(string Code, int Copies);

public readonly record struct HeldPair(int A, int B);

// ---- what RASK103 reports ----

public sealed record RecordHolder(HeldLabel Label, string Carrier);

public sealed record NullableHolder(HeldLabel? Label, string Carrier);

public readonly record struct StructHolder(HeldLabel Label, string Carrier);

public sealed record StructHeldHolder(HeldPair Label, string Carrier);

public sealed class ConstructorOnlyHolder
{
    public ConstructorOnlyHolder(HeldLabel label, string carrier)
    {
        Label = label;
        Carrier = carrier;
    }

    public HeldLabel Label { get; private set; }

    public string Carrier { get; private set; }
}

public sealed class TwoConstructorHolder
{
    public TwoConstructorHolder(HeldLabel label)
        : this(label, "")
    {
    }

    public TwoConstructorHolder(HeldLabel label, string carrier)
    {
        Label = label;
        Carrier = carrier;
    }

    public HeldLabel Label { get; private set; }

    public string Carrier { get; private set; }
}

// ---- what RASK103 leaves alone ----

/// <summary>The way out the message names: a private parameterless constructor and private setters.</summary>
public sealed class MembersHolder
{
    private MembersHolder() { }

    public HeldLabel Label { get; private set; } = new("", 0);

    public string Carrier { get; private set; } = "";

    public static MembersHolder Of(HeldLabel label, string carrier) => new() { Label = label, Carrier = carrier };
}

public sealed class OpenHolder
{
    public HeldLabel Label { get; set; } = new("", 0);

    public string Carrier { get; set; } = "";
}

public sealed record SpareConstructorHolder(HeldLabel Label, string Carrier)
{
    private SpareConstructorHolder()
        : this(new HeldLabel("", 0), "")
    {
    }
}

public sealed class OtherConstructorHolder
{
    public OtherConstructorHolder(string carrier) => Carrier = carrier;

    public HeldLabel Label { get; set; } = new("", 0);

    public string Carrier { get; private set; }
}

public struct PlainStructHolder
{
    public HeldLabel Label { get; set; }

    public string Carrier { get; set; }
}

/// <summary>One row holding <typeparamref name="THolder" /> — and holding it a level down, inside another value object.</summary>
public sealed class HolderRow<THolder>
{
    public int Id { get; set; }

    public THolder Route { get; set; } = default!;

    public HolderDeck<THolder> Deck { get; set; } = new();
}

public sealed class HolderDeck<THolder>
{
    public THolder Route { get; set; } = default!;

    public string Name { get; set; } = "";
}

/// <summary>Maps the holder as the generated registry does: a complex type whose several-values member is one too.</summary>
public sealed class HolderContext<THolder>(DbContextOptions<HolderContext<THolder>> options, bool nested) : DbContext(options)
    where THolder : notnull
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<HolderRow<THolder>>();

        if (nested)
        {
            row.Ignore(r => r.Route);
            row.ComplexProperty(r => r.Deck, b0 => b0.ComplexProperty(d => d.Route, b1 => b1.ComplexProperty("Label")));
            return;
        }

        row.Ignore(r => r.Deck);
        row.ComplexProperty(r => r.Route, b0 => b0.ComplexProperty("Label"));
    }
}

/// <summary>
/// The ground under RASK103: each shape it reports is one EF Core refuses to build a model for, and each shape
/// it leaves alone builds one and comes back whole. The shapes are the cases of the generator's own
/// <c>ConstructorBuiltHolderTests</c>.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ConstructorBuiltHolderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-holder-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Theory]
    [InlineData(typeof(RecordHolder))]
    [InlineData(typeof(NullableHolder))]
    [InlineData(typeof(StructHolder))]
    [InlineData(typeof(StructHeldHolder))]
    [InlineData(typeof(ConstructorOnlyHolder))]
    [InlineData(typeof(TwoConstructorHolder))]
    public void EF_Core_still_refuses_every_shape_RASK103_reports_and_the_day_it_builds_one_RASK103_is_retired(Type holder)
    {
        using var direct = Open(holder, nested: false);
        using var deeper = Open(holder, nested: true);

        var refusedDirect = Assert.Throws<InvalidOperationException>(() => direct.Model);
        var refusedDeeper = Assert.Throws<InvalidOperationException>(() => deeper.Model);

        // If this fails because nothing was thrown, EF Core binds a complex type to a constructor now: the shape
        // works, RASK103 is refusing code that would run, and it is time to retire it.
        Assert.Contains("No suitable constructor was found", refusedDirect.Message, StringComparison.Ordinal);
        Assert.Contains("Cannot bind", refusedDirect.Message, StringComparison.Ordinal);
        Assert.Contains("No suitable constructor was found", refusedDeeper.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(MembersHolder))]
    [InlineData(typeof(OpenHolder))]
    [InlineData(typeof(SpareConstructorHolder))]
    [InlineData(typeof(OtherConstructorHolder))]
    [InlineData(typeof(PlainStructHolder))]
    public void Every_shape_RASK103_leaves_alone_builds_a_model_directly_and_a_level_down(Type holder)
    {
        using var direct = Open(holder, nested: false);
        using var deeper = Open(holder, nested: true);

        var built = direct.Model.GetEntityTypes().Single().FindComplexProperty("Route");
        var builtDeeper = deeper.Model.GetEntityTypes().Single().FindComplexProperty("Deck")?.ComplexType.FindComplexProperty("Route");

        Assert.NotNull(built?.ComplexType.FindComplexProperty("Label"));
        Assert.NotNull(builtDeeper?.ComplexType.FindComplexProperty("Label"));
    }

    [Fact]
    public async Task The_way_out_the_message_names_is_saved_and_read_back_whole()
    {
        var options = new DbContextOptionsBuilder<HolderContext<MembersHolder>>().UseSqlite($"Data Source={_dbPath}").Options;
        await using (var writing = new HolderContext<MembersHolder>(options, nested: false))
        {
            await writing.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            writing.Add(new HolderRow<MembersHolder> { Route = MembersHolder.Of(new HeldLabel("AB-1", 2), "DHL") });
            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = new HolderContext<MembersHolder>(options, nested: false);
        var stored = await reading.Set<HolderRow<MembersHolder>>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal((new HeldLabel("AB-1", 2), "DHL"), (stored.Route.Label, stored.Route.Carrier));
    }

    [Fact]
    public async Task A_record_that_keeps_its_constructor_and_adds_a_private_parameterless_one_is_read_back_whole_too()
    {
        var options = new DbContextOptionsBuilder<HolderContext<SpareConstructorHolder>>().UseSqlite($"Data Source={_dbPath}").Options;
        await using (var writing = new HolderContext<SpareConstructorHolder>(options, nested: false))
        {
            await writing.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            writing.Add(new HolderRow<SpareConstructorHolder> { Route = new(new HeldLabel("AB-1", 2), "DHL") });
            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = new HolderContext<SpareConstructorHolder>(options, nested: false);
        var stored = await reading.Set<HolderRow<SpareConstructorHolder>>().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new SpareConstructorHolder(new HeldLabel("AB-1", 2), "DHL"), stored.Route);
    }

    // EF Core caches a model per context type, and the direct and the nested mapping share one type — so the
    // provider cache is off and each context builds its own.
    private static DbContext Open(Type holder, bool nested)
    {
        var context = typeof(HolderContext<>).MakeGenericType(holder);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(context))!;
        builder.UseSqlite("Data Source=:memory:").EnableServiceProviderCaching(false);
        return (DbContext)Activator.CreateInstance(context, builder.Options, nested)!;
    }
}
