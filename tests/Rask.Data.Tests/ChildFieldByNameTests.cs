using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A note in a casebook, a child the generic base holds.</summary>
public sealed class CaseNote : Entity<int>
{
    private CaseNote() { }

    public string Text { get; private set; } = "";

    internal static CaseNote Of(string text) => new() { Text = text };
}

/// <summary>
/// A generic abstract base holding its children in a field EF Core cannot match to the property by name, handed out
/// as a read-only view.
/// </summary>
/// <typeparam name="TSelf">The aggregate deriving from it.</typeparam>
public abstract class Casebook<TSelf> : Aggregate<int>, IFormModel
    where TSelf : Casebook<TSelf>
{
    private readonly List<CaseNote> _items = [];

    public string Title { get; private set; } = "";

    public IReadOnlyList<CaseNote> Notes => _items.AsReadOnly();

    public TSelf Add(string text)
    {
        _items.Add(CaseNote.Of(text));
        return (TSelf)this;
    }

    protected static TSelf Titled(TSelf casebook, string title)
    {
        casebook.Title = title;
        return casebook;
    }
}

/// <summary>A closed derived aggregate of the generic base.</summary>
public sealed class CivilCasebook : Casebook<CivilCasebook>
{
    private CivilCasebook() { }

    public static CivilCasebook For(string title) => Titled(new CivilCasebook(), title);

    public static void Configure(EntityTypeBuilder<CivilCasebook> builder) => builder.ToTable("CivilCasebooks");
}

/// <summary>An entry in a logbook.</summary>
public sealed class LogbookEntry : Entity<int>
{
    private LogbookEntry() { }

    public string Text { get; private set; } = "";

    internal static LogbookEntry Of(string text) => new() { Text = text };
}

/// <summary>An aggregate holding its own children in a field EF Core cannot match to the property by name.</summary>
public sealed class Logbook : Aggregate<int>, IFormModel
{
    private readonly List<LogbookEntry> _records = [];

    private Logbook() { }

    public string Title { get; private set; } = "";

    public IReadOnlyList<LogbookEntry> Entries => _records.AsReadOnly();

    public static Logbook For(string title) => new() { Title = title };

    public Logbook Add(string text)
    {
        _records.Add(LogbookEntry.Of(text));
        return this;
    }

    public static void Configure(EntityTypeBuilder<Logbook> builder) => builder.ToTable("Logbooks");
}

/// <summary>
/// Children behind a field whose name EF Core's convention does not pair with the property — <c>_items</c> behind
/// <c>Notes</c> — are written through that field, on the aggregate itself or on a generic abstract base.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ChildFieldByNameTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-child-field-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_navigation_is_mapped_to_the_field_the_generator_found()
    {
        await using var database = await StartDatabaseAsync();

        var model = database.Context.Model;

        Assert.Equal("_items", model.FindEntityType(typeof(CivilCasebook))!.FindNavigation(nameof(CivilCasebook.Notes))!.FieldInfo!.Name);
        Assert.Equal("_records", model.FindEntityType(typeof(Logbook))!.FindNavigation(nameof(Logbook.Entries))!.FieldInfo!.Name);
    }

    [Fact]
    public async Task Children_saved_through_the_aggregate_are_found_again()
    {
        await using var database = await StartDatabaseAsync();
        var casebook = CivilCasebook.For("Smith v Jones").Add("filed").Add("heard");
        var logbook = Logbook.For("ship").Add("sailed");

        await casebook.Save(cancellationToken: TestContext.Current.CancellationToken);
        await logbook.Save(cancellationToken: TestContext.Current.CancellationToken);
        var foundCasebook = (await CivilCasebook.Find(casebook.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var foundLogbook = (await Logbook.Find(logbook.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(["filed", "heard"], foundCasebook.Notes.Select(n => n.Text).Order(StringComparer.Ordinal));
        Assert.Equal(["sailed"], foundLogbook.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task Children_added_through_the_form_are_saved()
    {
        await using var database = await StartDatabaseAsync();
        var casebook = CivilCasebook.For("Smith v Jones").Add("filed");
        var logbook = Logbook.For("ship");
        await casebook.Save(cancellationToken: TestContext.Current.CancellationToken);
        await logbook.Save(cancellationToken: TestContext.Current.CancellationToken);

        var casebookForm = (await CivilCasebook.Model(casebook.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        casebookForm.Notes.Add(new CaseNoteModel { Text = "settled" });
        await CivilCasebook.Update(casebook.Id, casebookForm, cancellationToken: TestContext.Current.CancellationToken);
        var logbookForm = (await Logbook.Model(logbook.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        logbookForm.Entries.Add(new LogbookEntryModel { Text = "docked" });
        await Logbook.Update(logbook.Id, logbookForm, cancellationToken: TestContext.Current.CancellationToken);
        var storedCasebook = (await CivilCasebook.Find(casebook.Id, cancellationToken: TestContext.Current.CancellationToken))!;
        var storedLogbook = (await Logbook.Find(logbook.Id, cancellationToken: TestContext.Current.CancellationToken))!;

        Assert.Equal(["filed", "settled"], storedCasebook.Notes.Select(n => n.Text).Order(StringComparer.Ordinal));
        Assert.Equal(["docked"], storedLogbook.Entries.Select(e => e.Text));
    }

    [Fact]
    public async Task The_read_face_includes_the_children()
    {
        await using var database = await StartDatabaseAsync();
        var casebook = CivilCasebook.For("Smith v Jones").Add("filed");
        var logbook = Logbook.For("ship").Add("sailed");
        await casebook.Save(cancellationToken: TestContext.Current.CancellationToken);
        await logbook.Save(cancellationToken: TestContext.Current.CancellationToken);

        var casebookFace = await CivilCasebook.Include(c => c.Notes).First(c => c.Id == casebook.Id, TestContext.Current.CancellationToken);
        var logbookFace = await Logbook.Include(l => l.Entries).First(l => l.Id == logbook.Id, TestContext.Current.CancellationToken);

        Assert.Equal(["filed"], casebookFace!.Notes.Select(n => n.Text));
        Assert.Equal(["sailed"], logbookFace!.Entries.Select(e => e.Text));
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
