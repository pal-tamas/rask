using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests.Binders;

/// <summary>A stamp on a sheet: a value, so a sheet's stamps are one JSON column.</summary>
public sealed record Stamp(string Mark);

/// <summary>A binder whose sheets are keyed by the binder and the sheet's number, and refilled wholesale.</summary>
public sealed class Binder : Aggregate<int>
{
    private readonly List<BinderSheet> _sheets = [];

    public IReadOnlyList<BinderSheet> Sheets => _sheets;

    public static Binder With(params (int Number, string[] Marks)[] sheets)
    {
        var binder = new Binder();
        binder.Refill(sheets);
        return binder;
    }

    public void Refill(IEnumerable<(int Number, string[] Marks)> sheets)
    {
        _sheets.Clear();
        _sheets.AddRange(sheets.Select(static s => BinderSheet.For(s.Number, s.Marks)));
    }
}

/// <summary>A sheet keyed by its binder and its number, holding a collection of values.</summary>
public sealed partial class BinderSheet : Entity<(Binder, int Number)>
{
    private readonly List<Stamp> _stamps = [];

    public int Number { get; private set; }

    public IReadOnlyList<Stamp> Stamps => _stamps;

    internal static BinderSheet For(int number, IEnumerable<string> marks)
    {
        var sheet = new BinderSheet { Number = number };
        sheet._stamps.AddRange(marks.Select(static m => new Stamp(m)));
        return sheet;
    }
}

/// <summary>
/// A child keyed by several columns, replaced by a new instance of the same key, takes that instance's collection of
/// values onto the row the context tracks — now that such a collection is a complex collection, not an owned one.
/// </summary>
[Collection(Rask.Data.Tests.DataDbCollection.Name)]
public sealed class ReplacedChildValueCollectionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-binders-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_refilled_sheet_keeps_its_row_and_takes_the_new_stamps()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
        var binder = Binder.With((1, ["draft"]), (2, []));
        database.Context.Add(binder);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        binder.Refill([(1, ["draft", "signed"]), (2, [])]);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var fresh = new RaskDbContext(new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var sheets = await fresh.Set<BinderSheet>().OrderBy(s => s.Number).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal([["draft", "signed"], []], sheets.Select(s => s.Stamps.Select(m => m.Mark).ToArray()));
    }
}
