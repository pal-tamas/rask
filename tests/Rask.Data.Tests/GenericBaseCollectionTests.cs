using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A line of a filing, a child the generic base holds.</summary>
public sealed class FilingLine : Entity<int>
{
    private FilingLine() { }

    public string Text { get; private set; } = "";

    public decimal Amount { get; private set; }

    internal static FilingLine Of(string text, decimal amount) => new() { Text = text, Amount = amount };
}

/// <summary>
/// An abstract base that names its derived type — the curiously recurring shape — and holds its children and its
/// values behind private fields.
/// </summary>
/// <typeparam name="TSelf">The aggregate deriving from it.</typeparam>
public abstract class Filing<TSelf> : Aggregate<int, Model, Deletable>
    where TSelf : Filing<TSelf>
{
    private readonly List<FilingLine> _lines = [];
    private readonly List<string> _notes = [];

    public string Title { get; private set; } = "";

    public IReadOnlyList<FilingLine> Lines => _lines;

    public IReadOnlyList<string> Notes => _notes;

    public TSelf Add(string text, decimal amount)
    {
        _lines.Add(FilingLine.Of(text, amount));
        return (TSelf)this;
    }

    public TSelf Note(string note)
    {
        _notes.Add(note);
        return (TSelf)this;
    }

    protected static TSelf Titled(TSelf filing, string title)
    {
        filing.Title = title;
        return filing;
    }
}

/// <summary>A filing on value added, one closed derived aggregate of the generic base.</summary>
public sealed class VatReturn : Filing<VatReturn>
{
    private VatReturn() { }

    public string Period { get; private set; } = "";

    public static VatReturn For(string title, string period) => Titled(new VatReturn { Period = period }, title);

    public static void Configure(EntityTypeBuilder<VatReturn> builder) => builder.ToTable("VatReturns");
}

/// <summary>A second closed derived aggregate, so each gets the base's collections of its own.</summary>
public sealed class DutyReturn : Filing<DutyReturn>
{
    private DutyReturn() { }

    public static DutyReturn For(string title) => Titled(new DutyReturn(), title);

    public static void Configure(EntityTypeBuilder<DutyReturn> builder) => builder.ToTable("DutyReturns");
}

/// <summary>
/// A collection behind a private field of a GENERIC abstract base maps on each closed derived aggregate exactly as one
/// on a plain abstract base does: written by the aggregate, found again, and read through the read face.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class GenericBaseCollectionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-generic-base-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Each_closed_derived_aggregate_maps_the_generic_bases_field_collections()
    {
        await using var database = await StartDatabaseAsync();

        var model = database.Context.Model;

        foreach (var filing in new[] { model.FindEntityType(typeof(VatReturn))!, model.FindEntityType(typeof(DutyReturn))! })
        {
            Assert.Equal("_lines", filing.FindNavigation(nameof(VatReturn.Lines))!.FieldInfo!.Name);
            Assert.True(filing.FindProperty(nameof(VatReturn.Notes))!.IsPrimitiveCollection);
        }
    }

    [Fact]
    public async Task Lines_written_through_the_generic_base_are_found_again_with_the_aggregate()
    {
        await using var database = await StartDatabaseAsync();
        var vat = VatReturn.For("Q3", "2026-Q3").Add("sales", 270m).Add("refund", -27m).Note("filed");
        var duty = DutyReturn.For("fuel").Add("diesel", 40m);
        await vat.Save(cancellationToken: TestContext.Current.CancellationToken);
        await duty.Save(cancellationToken: TestContext.Current.CancellationToken);

        var foundVat = (await VatReturn.Find(vat.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var foundDuty = (await DutyReturn.Find(duty.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal([("sales", 270m), ("refund", -27m)], foundVat.Lines.OrderByDescending(l => l.Amount).Select(l => (l.Text, l.Amount)));
        Assert.Equal(["filed"], foundVat.Notes);
        Assert.Equal([("diesel", 40m)], foundDuty.Lines.Select(l => (l.Text, l.Amount)));
    }

    [Fact]
    public async Task The_read_face_of_a_closed_derived_aggregate_reads_the_generic_bases_lines_and_values()
    {
        await using var database = await StartDatabaseAsync();
        var vat = VatReturn.For("Q3", "2026-Q3").Add("sales", 270m).Note("filed");
        await vat.Save(cancellationToken: TestContext.Current.CancellationToken);

        var face = await VatReturn.Include(l => l.Lines).First(l => l.Id == vat.Id, TestContext.Current.CancellationToken);
        var noted = await VatReturn.Where(l => l.Notes.Contains("filed")).Select(l => l.Period);

        Assert.Equal([("sales", 270m)], face!.Lines.Select(l => (l.Text, l.Amount)));
        Assert.Equal(["2026-Q3"], noted);
    }

    [Fact]
    public async Task The_form_model_of_a_closed_derived_aggregate_writes_the_generic_bases_collections_back()
    {
        await using var database = await StartDatabaseAsync();
        var vat = VatReturn.For("Q3", "2026-Q3").Add("sales", 270m).Note("filed");
        await vat.Save(cancellationToken: TestContext.Current.CancellationToken);

        var form = (await VatReturn.Model(vat.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var readNotes = form.Notes.ToList();
        var readLines = form.Lines.Select(l => l.Text).ToList();
        form.Notes = ["filed", "paid"];
        form.Lines.Add(new FilingLineModel { Text = "late fee", Amount = 5m });
        await VatReturn.Update(vat.Id, form, cancellationToken: TestContext.Current.CancellationToken);
        var stored = (await VatReturn.Find(vat.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(["filed"], readNotes);
        Assert.Equal(["sales"], readLines);
        Assert.Equal(["filed", "paid"], stored.Notes);
        Assert.Equal(["late fee", "sales"], stored.Lines.Select(l => l.Text).Order(StringComparer.Ordinal));
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
