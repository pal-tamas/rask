using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

// Every save asks the domain first: the attributes of what it changes, the type's own IValidatableObject, Validate()
// and ValidateDelete() — and refuses with one RaskValidationException, each failure under the field a form names.
[Collection(DataDbCollection.Name)]
public sealed class DomainValidationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-domain-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_aggregate_is_refused_with_each_attribute_failure_under_its_field()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("", 0);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => voyage.Save(cancellationToken: Token));

        Assert.Equal(["Seats", "Title"], refused.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(await database.Context.Set<Voyage>().ToListAsync(Token));
    }

    [Fact]
    public async Task A_save_asks_only_the_properties_it_changes_so_an_old_row_still_saves()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);
        await database.Context.Set<Voyage>().Where(v => v.Id == voyage.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Title, "A title from before the rule"), Token);
        var stored = (await Voyage.Find(voyage.Id, Token))!;

        stored.Book(5);
        await stored.Save(cancellationToken: Token);
        stored.Rename("Still far too long");
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => stored.Save(cancellationToken: Token));

        Assert.Equal(["Title"], refused.Errors.Keys);
        Assert.Equal(5, (await database.Load<Voyage>(voyage.Id, Token))!.Seats);
    }

    [Fact]
    public async Task A_refused_save_writes_nothing_and_the_fixed_aggregate_then_saves()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        await voyage.Save(cancellationToken: Token);
        var stored = (await Voyage.Find(voyage.Id, Token))!;

        stored.Book(500);
        await Assert.ThrowsAsync<RaskValidationException>(() => stored.Save(cancellationToken: Token));
        stored.Book(50);
        await stored.Save(cancellationToken: Token);

        var saved = await database.Load<Voyage>(voyage.Id, Token);
        Assert.Equal(50, saved!.Seats);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task The_aggregates_Validate_runs_when_only_a_row_of_it_changed()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        var leg = voyage.Add("Oslo", 2);
        voyage.Seal();
        await voyage.Save(cancellationToken: Token);
        var stored = (await Voyage.Find(voyage.Id, Token))!;

        stored.Remove(stored.Legs.Single(l => l.Id == leg.Id));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => stored.Save(cancellationToken: Token));

        Assert.Equal("A sealed voyage needs a leg.", Assert.Single(refused.Errors["Legs"]));
    }

    [Fact]
    public async Task A_rows_failures_are_shown_under_the_row_they_are_about()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        voyage.Add("Oslo", 2);
        voyage.Add("Copenhagen", 3).Stay(-1);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => voyage.Save(cancellationToken: Token));

        Assert.Equal(["Legs[1].Days", "Legs[1].Port"], refused.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("A leg cannot take negative days.", Assert.Single(refused.Errors["Legs[1].Days"]));
    }

    [Fact]
    public async Task ValidateDelete_refuses_a_delete_and_the_aggregate_stays()
    {
        await using var database = await StartDatabaseAsync();
        var voyage = Voyage.Plan("North", 2);
        voyage.Add("Oslo", 2);
        voyage.Seal();
        await voyage.Save(cancellationToken: Token);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Voyage.Delete(voyage.Id, cancellationToken: Token));

        Assert.Equal("A sealed voyage is kept.", Assert.Single(refused.Failures).Message);
        Assert.NotNull(await database.Load<Voyage>(voyage.Id, Token));
    }

    [Fact]
    public async Task A_save_says_what_Create_of_the_model_says_for_the_same_attribute()
    {
        await using var database = await StartDatabaseAsync();
        var model = new VoyageModel { Title = "Far too long a title", Seats = 2 };

        var fromModel = await Assert.ThrowsAsync<RaskValidationException>(() => Voyage.Create(model, cancellationToken: Token));
        var fromSave = await Assert.ThrowsAsync<RaskValidationException>(() => Voyage.Plan("Far too long a title", 2).Save(cancellationToken: Token));

        Assert.Equal(fromModel.Errors["Title"], fromSave.Errors["Title"]);
    }

    [Fact]
    public async Task An_IValidatableObject_aggregate_is_asked_on_every_save()
    {
        await using var database = await StartDatabaseAsync();
        var cabin = Cabin.Fit("Aft", 6);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => GeneratedModelWrites.Create(cabin, cancellationToken: Token));

        Assert.Equal("At most four berths.", Assert.Single(refused.Errors["Berths"]));
    }

    [Fact]
    public async Task An_attribute_resolves_the_services_of_the_work_in_progress()
    {
        await using var database = await StartDatabaseAsync();
        await using var services = new ServiceCollection().AddSingleton(new ForbiddenWords("Steerage")).BuildServiceProvider();
        using var work = Ambient.Enter(services);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => GeneratedModelWrites.Create(Cabin.Fit("Steerage", 2), cancellationToken: Token));

        Assert.Equal("That word is forbidden.", Assert.Single(refused.Errors["Name"]));
    }

    [Fact]
    public async Task Create_of_the_model_asks_the_models_own_Validate()
    {
        await using var database = await StartDatabaseAsync();
        var model = new CharterModel { Name = "Beagle", Guests = 20 };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Charter.Create(model, cancellationToken: Token));

        Assert.Equal("At most twelve guests.", Assert.Single(refused.Errors["Guests"]));
    }

    [Fact]
    public async Task An_async_attribute_refuses_Create_of_the_model_through_its_save_and_writes_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var taken = new CharterModel { Name = "Endeavour", Guests = 2 };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => Charter.Create(taken, cancellationToken: Token));

        Assert.Equal("Name is taken.", Assert.Single(refused.Errors["Name"]));
        Assert.Empty(await database.Context.Set<Charter>().ToListAsync(Token));
    }

    [Fact]
    public async Task Create_of_the_model_hands_an_attribute_the_services_of_the_work_in_progress()
    {
        await using var database = await StartDatabaseAsync();
        await using var services = new ServiceCollection().AddSingleton(new ForbiddenWords("Bilge")).BuildServiceProvider();
        using var work = Ambient.Enter(services);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Mooring.Create(new MooringModel { Name = "Bilge" }, cancellationToken: Token));

        Assert.Equal("That word is forbidden.", Assert.Single(refused.Errors["Name"]));
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
