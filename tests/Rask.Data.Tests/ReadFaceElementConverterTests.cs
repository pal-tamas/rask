using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A value object of one value, held in a collection.</summary>
public sealed record SkidBarcode(string Value);

/// <summary>How a skid is handled, stored by name inside its collection.</summary>
public enum SkidHandling
{
    Upright,
    Fragile,
    Cold,
}

/// <summary>
/// An aggregate holding three collections of values whose ELEMENT is read through a converter: one-value value
/// objects, enums stored as their names, and strings sealed by the app's own converter.
/// </summary>
public sealed class Skid : Aggregate<int, Deletable>
{
    private readonly List<SkidBarcode> _barcodes = [];
    private readonly List<SkidHandling> _handling = [];
    private readonly List<string> _seals = [];

    private Skid() { }

    public string Label { get; private set; } = "";

    public IReadOnlyList<SkidBarcode> Barcodes => _barcodes;

    public IReadOnlyList<SkidHandling> Handling => _handling;

    public IReadOnlyList<string> Seals => _seals;

    public static Skid Packed(string label, string[] barcodes, SkidHandling[] handling, string[] seals)
    {
        var skid = new Skid { Label = label };
        skid._barcodes.AddRange(barcodes.Select(static b => new SkidBarcode(b)));
        skid._handling.AddRange(handling);
        skid._seals.AddRange(seals);
        return skid;
    }

    public static void Configure(EntityTypeBuilder<Skid> builder)
    {
        builder.ToTable("Skids");
        builder.PrimitiveCollection(c => c.Handling).HasField("_handling").ElementType().HasConversion<string>();
        builder.PrimitiveCollection(c => c.Seals).HasField("_seals").ElementType().HasConversion(CofferConverters.Sealed);
    }
}

/// <summary>
/// A collection of values is one column, and the read face reads that column exactly as the write side writes it —
/// whatever converter the write side put on each ELEMENT.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ReadFaceElementConverterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-read-elements-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_list_of_one_value_value_objects_reads_back_the_values_the_aggregate_wrote()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Skid.Packed("A", ["B-1", "B-2"], [], []));

        var barcodes = await Skid.Where(c => c.Id == id).Select(c => c.Barcodes).First(TestContext.Current.CancellationToken);

        Assert.Equal([new SkidBarcode("B-1"), new SkidBarcode("B-2")], barcodes);
    }

    [Fact]
    public async Task A_list_of_enums_stored_as_their_names_reads_back_and_is_found_by_an_element()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Skid.Packed("cold", [], [SkidHandling.Fragile, SkidHandling.Cold], []));
        await SaveAsync(Skid.Packed("plain", [], [SkidHandling.Upright], []));

        var handling = await Skid.Where(c => c.Label == "cold").Select(c => c.Handling).First(TestContext.Current.CancellationToken);
        var cold = await Skid.Where(c => c.Handling.Contains(SkidHandling.Cold)).Select(c => c.Label);

        Assert.Equal([SkidHandling.Fragile, SkidHandling.Cold], handling);
        Assert.Equal(["cold"], cold);
    }

    [Fact]
    public async Task A_list_stored_through_the_apps_element_converter_reads_back_unconverted()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Skid.Packed("sealed", [], [], ["S-1", "S-2"]));

        var seals = await Skid.Where(c => c.Id == id).Select(c => c.Seals).First(TestContext.Current.CancellationToken);
        var raw = await database.Context.Database
            .SqlQuery<string>($"SELECT Seals AS Value FROM Skids WHERE Id = {id}")
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["S-1", "S-2"], seals);
        Assert.Contains(CofferConverters.Seal("S-1"), raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_aggregate_found_again_holds_every_element_it_was_saved_with()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Skid.Packed("round", ["B-9"], [SkidHandling.Cold], ["S-9"]));

        var found = (await Skid.Find(id, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal([new SkidBarcode("B-9")], found.Barcodes);
        Assert.Equal([SkidHandling.Cold], found.Handling);
        Assert.Equal(["S-9"], found.Seals);
    }

    private static async Task<int> SaveAsync(Skid skid)
    {
        await skid.Save(cancellationToken: TestContext.Current.CancellationToken);
        return skid.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
