using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A one-value struct an abstract base holds as <c>T?</c>.</summary>
public readonly record struct FilingBarcode(string Value);

/// <summary>A one-value class an abstract base holds, required.</summary>
public sealed record FilingOffice(string Value);

/// <summary>A value object of several values an abstract base holds.</summary>
public sealed record FilingPeriod(int Year, int Quarter);

/// <summary>
/// An abstract base two aggregates share, holding every shape of value: a nullable one-value struct, a
/// required one-value class, one of several values, and a collection of values behind a private field.
/// </summary>
public abstract class Filing : Aggregate<int>, IFormModel, IDeletable
{
    private readonly List<string> _tags = [];

    public string Title { get; private set; } = "";

    public FilingBarcode? Barcode { get; private set; }

    public FilingOffice Office { get; private set; } = new("");

    public FilingPeriod Period { get; private set; } = new(2026, 1);

    public IReadOnlyList<string> Tags => _tags;

    public void Record(FilingBarcode? barcode) => Barcode = barcode;

    protected static T Filled<T>(T filing, string title, string office, string? barcode, params string[] tags)
        where T : Filing
    {
        (filing.Title, filing.Office, filing.Period) = (title, new FilingOffice(office), new FilingPeriod(2026, 3));
        filing.Barcode = barcode is null ? null : new FilingBarcode(barcode);
        filing._tags.AddRange(tags);
        return filing;
    }
}

/// <summary>A second abstract level, so a value object two bases up is reached too.</summary>
public abstract class TaxFiling : Filing
{
    public FilingOffice? Receipt { get; private set; }

    protected static T Received<T>(T filing, string? receipt)
        where T : TaxFiling
    {
        filing.Receipt = receipt is null ? null : new FilingOffice(receipt);
        return filing;
    }
}

/// <summary>A filing two bases down.</summary>
public sealed class GasFiling : TaxFiling
{
    private GasFiling() { }

    public int Litres { get; private set; }

    public static GasFiling Of(string title, string office, string? barcode = null, string? receipt = null) =>
        Received(Filled(new GasFiling { Litres = 40 }, title, office, barcode, "gas"), receipt);

    public static void Configure(EntityTypeBuilder<GasFiling> builder) => builder.ToTable("GasFilings");
}

/// <summary>A filing one base down.</summary>
public sealed class ForeignFiling : Filing
{
    private ForeignFiling() { }

    public string Country { get; private set; } = "";

    public static ForeignFiling Of(string title, string office, string? barcode = null) =>
        Filled(new ForeignFiling { Country = "AT" }, title, office, barcode, "abroad", "eu");

    public static void Configure(EntityTypeBuilder<ForeignFiling> builder) => builder.ToTable("ForeignFilings");
}

/// <summary>
/// A value object an abstract base declares is mapped, read and carried exactly as one the aggregate declares
/// itself — at any depth, on every aggregate that derives from it.
/// </summary>
/// <remarks>
/// Rask maps the concrete aggregates only: the abstract base is never an entity of the model, so each derived
/// aggregate is its own table holding the base's columns. The base's members used to be left out of the
/// generated mapping, and EF Core refused the model at startup for the one-value value object it could not
/// store — until the app converted it by hand in its own Configure.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class InheritedValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-inherited-vo-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Every_derived_aggregate_stores_the_bases_value_objects_as_its_own_columns()
    {
        await using var database = await StartDatabaseAsync();

        var model = database.Context.Model;
        var gas = model.FindEntityType(typeof(GasFiling))!;
        var foreign = model.FindEntityType(typeof(ForeignFiling))!;

        Assert.Null(model.FindEntityType(typeof(Filing)));
        Assert.Null(model.FindEntityType(typeof(TaxFiling)));
        foreach (var filing in new[] { gas, foreign })
        {
            var barcode = filing.FindProperty(nameof(Filing.Barcode))!;
            var office = filing.FindProperty(nameof(Filing.Office))!;
            Assert.Equal(("Barcode", typeof(string), true), (barcode.GetColumnName(), barcode.GetValueConverter()!.ProviderClrType, barcode.IsNullable));
            Assert.Equal(("Office", typeof(string), false), (office.GetColumnName(), office.GetValueConverter()!.ProviderClrType, office.IsNullable));
            Assert.NotNull(filing.FindComplexProperty(nameof(Filing.Period)));
            Assert.True(filing.FindProperty(nameof(Filing.Tags))!.IsPrimitiveCollection);
        }

        var receipt = gas.FindProperty(nameof(TaxFiling.Receipt))!;
        Assert.Equal((typeof(string), true), (receipt.GetValueConverter()!.ProviderClrType, receipt.IsNullable));
    }

    [Fact]
    public async Task Both_derived_aggregates_save_and_read_back_what_the_base_holds()
    {
        await using var database = await StartDatabaseAsync();
        var gas = GasFiling.Of("Q3 gas", "Budapest", barcode: "B-1", receipt: "R-9");
        var bare = GasFiling.Of("Q3 bare", "Győr");
        var foreign = ForeignFiling.Of("Q3 abroad", "Wien", barcode: "F-2");

        await gas.Save(cancellationToken: TestContext.Current.CancellationToken);
        await bare.Save(cancellationToken: TestContext.Current.CancellationToken);
        await foreign.Save(cancellationToken: TestContext.Current.CancellationToken);

        Filing storedGas = (await database.Load<GasFiling>(gas.Id, TestContext.Current.CancellationToken))!;
        Filing storedBare = (await database.Load<GasFiling>(bare.Id, TestContext.Current.CancellationToken))!;
        Filing storedForeign = (await database.Load<ForeignFiling>(foreign.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal((new FilingBarcode("B-1"), new FilingOffice("Budapest"), new FilingPeriod(2026, 3)), (storedGas.Barcode, storedGas.Office, storedGas.Period));
        Assert.Equal(["gas"], storedGas.Tags);
        Assert.Equal(new FilingOffice("R-9"), ((GasFiling)storedGas).Receipt);
        Assert.Null(storedBare.Barcode);
        Assert.Null(((GasFiling)storedBare).Receipt);
        Assert.Equal((new FilingBarcode("F-2"), new FilingOffice("Wien")), (storedForeign.Barcode, storedForeign.Office));
        Assert.Equal(["abroad", "eu"], storedForeign.Tags);
    }

    [Fact]
    public async Task A_value_changed_through_the_base_is_saved_and_cleared()
    {
        await using var database = await StartDatabaseAsync();
        var filing = ForeignFiling.Of("Q3 abroad", "Wien");
        await filing.Save(cancellationToken: TestContext.Current.CancellationToken);

        var held = (await ForeignFiling.Find(filing.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        held.Record(new FilingBarcode("NAV-7"));
        await held.Save(cancellationToken: TestContext.Current.CancellationToken);
        var recorded = (await database.Load<ForeignFiling>(filing.Id, TestContext.Current.CancellationToken))!.Barcode;
        held.Record(null);
        await held.Save(cancellationToken: TestContext.Current.CancellationToken);
        var cleared = (await database.Load<ForeignFiling>(filing.Id, TestContext.Current.CancellationToken))!.Barcode;

        Assert.Equal(new FilingBarcode("NAV-7"), recorded);
        Assert.Null(cleared);
    }

    [Fact]
    public async Task The_read_face_of_each_derived_aggregate_shows_the_bases_values_as_the_primitives_they_hold()
    {
        await using var database = await StartDatabaseAsync();
        await GasFiling.Of("Ada", "Budapest", barcode: "B-1", receipt: "R-1").Save(cancellationToken: TestContext.Current.CancellationToken);
        await GasFiling.Of("Bob", "Győr").Save(cancellationToken: TestContext.Current.CancellationToken);
        await ForeignFiling.Of("Cy", "Wien", barcode: "F-1").Save(cancellationToken: TestContext.Current.CancellationToken);

        var coded = await GasFiling.Where(f => f.Barcode == "B-1").Select(f => f.Receipt);
        var uncoded = await GasFiling.Where(f => f.Barcode == null).Select(f => f.Office);
        var abroad = await ForeignFiling.Where(f => f.Office == "Wien").Select(f => f.Barcode);

        Assert.Equal(["R-1"], coded);
        Assert.Equal(["Győr"], uncoded);
        Assert.Equal(["F-1"], abroad);
    }

    [Fact]
    public async Task The_form_model_of_a_derived_aggregate_carries_the_bases_values_and_writes_them_back()
    {
        await using var database = await StartDatabaseAsync();

        var created = await GasFiling.Create(
            new GasFilingModel
            {
                Title = "Q4",
                Office = "Pécs",
                Barcode = "B-4",
                Receipt = null,
                Period = new() { Year = 2026, Quarter = 4 },
                Tags = ["winter"],
                Litres = 12,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var form = (await GasFiling.Model(created.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var read = (form.Barcode, form.Office, form.Period?.Quarter, form.Tags.ToList());
        form.Barcode = null;
        form.Receipt = "R-4";
        await GasFiling.Update(created.Id, form, cancellationToken: TestContext.Current.CancellationToken);
        var stored = (await database.Load<GasFiling>(created.Id, TestContext.Current.CancellationToken))!;

        Assert.Equal(("B-4", "Pécs", 4), (read.Barcode, read.Office, read.Quarter));
        Assert.Equal(["winter"], read.Item4);
        Assert.Null(stored.Barcode);
        Assert.Equal(new FilingOffice("R-4"), stored.Receipt);
        Assert.Equal(["winter"], stored.Tags);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
