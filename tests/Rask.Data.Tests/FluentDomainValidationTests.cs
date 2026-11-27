using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

// The rules an aggregate states in its own static Rules(InlineValidator<T>) are asked by every save exactly where its
// DataAnnotations attributes would be — every rule of a new row, only the changed members of a stored one, each row's
// own under its place in the form — and give the columns what the attribute with the same rule would.
public sealed class FluentDomainValidationTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_aggregate_is_refused_with_each_rule_failure_under_its_field()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = Harbour.Open("", "nowhere");

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => harbour.Save(cancellationToken: Token));

        Assert.Equal(["Email", "Name"], refused.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("A harbour has a name.", Assert.Single(refused.Errors["Name"]));
        Assert.Empty(await database.Context.Set<Harbour>().ToListAsync(Token));
    }

    [Fact]
    public async Task A_save_asks_only_the_members_it_changes_so_an_old_row_still_saves()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = Harbour.Open("Bergen");
        await harbour.Save(cancellationToken: Token);
        await database.Context.Set<Harbour>().Where(h => h.Id == harbour.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.Name, "A name from before the rule"), Token);
        var stored = (await Harbour.Find(harbour.Id, Token))!;

        stored.Write("port@bergen.no");
        await stored.Save(cancellationToken: Token);
        stored.Write("not an address");
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => stored.Save(cancellationToken: Token));

        Assert.Equal(["Email"], refused.Errors.Keys);
        Assert.Equal("port@bergen.no", (await database.Load<Harbour>(harbour.Id, Token))!.Email);
    }

    [Fact]
    public async Task A_rows_rules_run_with_the_aggregate_under_the_row_they_are_about()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = Harbour.Open("Bergen");
        harbour.Add(120);
        harbour.Add(0);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => harbour.Save(cancellationToken: Token));

        Assert.Equal("A quay has a length.", Assert.Single(refused.Errors["Quays[1].Length"]));
    }

    [Fact]
    public async Task An_awaited_rule_refuses_the_save()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = Harbour.Open("Bergen");

        harbour.Recode("TAKEN");
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => harbour.Save(cancellationToken: Token));

        Assert.Equal("That code is taken.", Assert.Single(refused.Errors["Code"]));
    }

    [Fact]
    public async Task Create_of_the_model_is_refused_by_the_aggregates_rules_through_its_save()
    {
        await using var database = await StartDatabaseAsync();
        var model = new HarbourModel { Name = "Bergen", Email = "nowhere", Code = "TAKEN", Region = "North" };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Harbour.Create(model, cancellationToken: Token));

        Assert.Equal("Not an address.", Assert.Single(refused.Errors["Email"]));
        Assert.Equal("That code is taken.", Assert.Single(refused.Errors["Code"]));
        Assert.Empty(await database.Context.Set<Harbour>().ToListAsync(Token));
    }

    [Fact]
    public async Task The_rules_give_the_columns_what_the_attribute_twin_would()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = database.Context.Model.FindEntityType(typeof(Harbour))!;

        var name = harbour.FindProperty(nameof(Harbour.Name))!;
        var code = harbour.FindProperty(nameof(Harbour.Code))!;
        var depth = harbour.FindProperty(nameof(Harbour.Depth))!;

        Assert.Equal(20, name.GetMaxLength());
        Assert.Equal(5, code.GetMaxLength());
        Assert.False(code.IsNullable);
        Assert.True(harbour.FindProperty(nameof(Harbour.Email))!.IsNullable);
        Assert.Equal((8, 2), (depth.GetPrecision(), depth.GetScale()));
    }

    [Fact]
    public async Task An_attribute_on_the_property_wins_over_the_rule_for_its_column()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = database.Context.Model.FindEntityType(typeof(Harbour))!;

        var region = harbour.FindProperty(nameof(Harbour.Region))!;

        Assert.Equal(50, region.GetMaxLength());
    }

    [Fact]
    public async Task A_value_objects_own_rule_is_not_asked_of_what_a_row_already_holds()
    {
        await using var database = await StartDatabaseAsync();
        var harbour = Harbour.Open("Bergen");
        harbour.Mark(HarbourCode.Create("BRG"));
        await harbour.Save(cancellationToken: Token);
        await Overwrite(database, harbour.Id, "lower");

        var loaded = await database.Load<Harbour>(harbour.Id, Token);

        Assert.Equal("lower", loaded!.Sign!.Value);
        Assert.Contains("A sign is in capitals.", Assert.Throws<RaskValidationException>(() => HarbourCode.Create("lower")).Errors.Values.SelectMany(static m => m));
    }

    private static async Task Overwrite(TestDatabase database, Guid id, string sign)
    {
        var type = database.Context.Model.FindEntityType(typeof(Harbour))!;
        var table = StoreObjectIdentifier.Table(type.GetTableName()!, type.GetSchema());
        var column = type.FindComplexProperty(nameof(Harbour.Sign))?.ComplexType.GetProperties().Single().GetColumnName(table)
                     ?? type.FindProperty(nameof(Harbour.Sign))!.GetColumnName(table)!;
        var key = type.FindProperty(nameof(Harbour.Id))!.GetColumnName(table)!;

#pragma warning disable EF1002 // the names are the model's own, the values are parameters
        await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table.Name}\" SET \"{column}\" = {{0}} WHERE \"{key}\" = {{1}}", [sign, id], Token);
#pragma warning restore EF1002
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), _clock);
}
