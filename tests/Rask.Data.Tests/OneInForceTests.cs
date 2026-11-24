using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A return that is filed, unfiled and filed again: its filings are the history, and one at most is in force.</summary>
public sealed class TaxReturn : Aggregate<Guid, Versioned, OneInForce<ReturnFiled>>
{
    private readonly List<ReturnFiled> _filings = [];

    private TaxReturn() { }

    public string Period { get; private set; } = "";

    public IReadOnlyList<ReturnFiled> Filings => _filings;

    public static TaxReturn For(string period) => new() { Id = Guid.NewGuid(), Period = period };

    public void File(DateTime at, string by) => _filings.Put(ReturnFiled.Filed(at, by), end: f => f.Unfile(at));

    public void Unfile(DateTime at) => _filings.End(f => f.Unfile(at));

    // What a careless method would do: add one without ending the one in force.
    public void FileOver(DateTime at, string by) => _filings.Add(ReturnFiled.Filed(at, by));
}

/// <summary>A filing, in force until it is unfiled.</summary>
public sealed class ReturnFiled : Entity<Guid>
{
    public const string AlreadyFiled = "This return is already filed.";

    private ReturnFiled() { }

    public DateTime FiledAt { get; private set; }

    public string FiledBy { get; private set; } = "";

    public DateTime? UnfiledAt { get; private set; }

    internal static ReturnFiled Filed(DateTime at, string by) => new() { Id = Guid.NewGuid(), FiledAt = at, FiledBy = by };

    internal void Unfile(DateTime at) => UnfiledAt = at;

    public static void Configure(EntityTypeBuilder<ReturnFiled> builder) =>
        builder.InForceWhile(f => f.UnfiledAt == null, AlreadyFiled);
}

/// <summary>
/// F-32: <see cref="OneInForce{TChild}" /> keeps one child in force — a partial unique index the database holds, the
/// history beside it, and InForce / Put / End asking the same condition in memory.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class OneInForceTests : IDisposable
{
    private static readonly DateTime March = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-one-in-force-{Guid.NewGuid():N}.db");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_child_gets_a_unique_index_on_its_parent_over_the_rows_in_force()
    {
        await using var database = await StartDatabaseAsync();

        var filing = database.Context.Model.FindEntityType(typeof(ReturnFiled))!;

        var index = Assert.Single(filing.GetIndexes(), i => i.IsUnique);
        Assert.Equal(["TaxReturnId"], index.Properties.Select(p => p.Name));
        Assert.Equal("\"UnfiledAt\" IS NULL", index.GetFilter());
        Assert.Null(filing.FindAnnotation(InForceBuilderExtensions.Annotation));
    }

    [Fact]
    public async Task Filing_again_ends_the_one_in_force_and_keeps_it_as_history()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync();

        await ChangeAsync(id, r => r.File(March.AddDays(1), "ada"));

        var stored = (await TaxReturn.Find(id, Token))!;
        Assert.Equal(2, stored.Filings.Count);
        Assert.Equal("ada", stored.Filings.InForce()!.FiledBy);
        Assert.Single(stored.Filings, f => f.UnfiledAt == March.AddDays(1));
    }

    [Fact]
    public async Task Unfiling_leaves_none_in_force()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync();

        await ChangeAsync(id, r => r.Unfile(March.AddDays(2)));

        var stored = (await TaxReturn.Find(id, Token))!;
        Assert.Null(stored.Filings.InForce());
        Assert.Single(stored.Filings);
    }

    [Fact]
    public async Task A_second_one_put_in_force_beside_the_first_is_refused_by_the_database()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync();

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => ChangeAsync(id, r => r.FileOver(March.AddDays(1), "eve")));

        Assert.Contains(ReturnFiled.AlreadyFiled, refused.Errors.Values.SelectMany(static m => m));
        Assert.Equal(1, await database.Context.Set<ReturnFiled>().CountAsync(Token));
    }

    [Fact]
    public async Task Put_refuses_a_child_that_is_not_in_force_itself()
    {
        await using var database = await StartDatabaseAsync();
        List<ReturnFiled> filings = [];
        var ended = ReturnFiled.Filed(March, "ada");
        ended.Unfile(March);

        var refused = Assert.Throws<ArgumentException>(() => filings.Put(ended, end: f => f.Unfile(March)));

        Assert.Equal("next", refused.ParamName);
    }

    private static async Task<Guid> SeedAsync()
    {
        var taxReturn = TaxReturn.For("2026-02");
        taxReturn.File(March, "bob");
        await taxReturn.Save(cancellationToken: Token);
        return taxReturn.Id;
    }

    private static async Task ChangeAsync(Guid id, Action<TaxReturn> change)
    {
        var taxReturn = (await TaxReturn.Find(id, Token))!;
        change(taxReturn);
        await taxReturn.Save(cancellationToken: Token);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
