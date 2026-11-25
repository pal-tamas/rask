using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Children<TEntity, TSpec>: an aggregate's children as one read-only property, made what the form's rows say by Sync —
// and mapped by EF Core as the aggregate's collection, so the save writes exactly what Sync changed.
public sealed class ChildrenSyncTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Sync_updates_the_children_its_specs_name_adds_the_rest_and_lets_go_of_the_unnamed()
    {
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10), new(null, "B", 20), new(null, "C", 30)]);
        var (a, b, c) = (cargo.Parts[0], cargo.Parts[1], cargo.Parts[2]);

        var sync = cargo.Change([new(c.Id, "C", 35), new(null, "D", 5), new(a.Id, "A", 10)]);

        Assert.Equal([c, cargo.Parts[1], a], cargo.Parts);
        Assert.Equal("D", Assert.Single(sync.Added).Hold);
        Assert.Equal([c], sync.Changed);
        Assert.Equal([b], sync.Removed);
        Assert.Equal([c, sync.Added[0]], sync.Touched);
        Assert.True(b.LetGone);
        Assert.Equal(35, c.Tonnes);
    }

    [Fact]
    public void Two_specs_naming_one_child_are_refused_and_the_children_stay()
    {
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10)]);
        var a = cargo.Parts[0];

        var refused = Assert.Throws<ArgumentException>(() => cargo.Change([new(a.Id, "A", 11), new(a.Id, "A", 12)]));

        Assert.Equal("specs", refused.ParamName);
        Assert.Equal([a], cargo.Parts);
    }

    [Fact]
    public void A_key_of_another_type_than_the_childrens_ids_is_refused_rather_than_replacing_every_child()
    {
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10)]);
        var a = cargo.Parts[0];

        var refused = Assert.Throws<ArgumentException>(() =>
            cargo.Parts.Sync([new CargoPartSpec(a.Id, "A", 11)], s => s.Id?.ToString(), CargoPart.For, (_, _) => { }));

        Assert.Equal("key", refused.ParamName);
        Assert.Equal([a], cargo.Parts);
    }

    [Fact]
    public async Task A_new_aggregates_children_are_inserted_and_load_back_into_its_property()
    {
        await using var database = await StartDatabaseAsync();
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10), new(null, "B", 20)]);

        await cargo.Save(cancellationToken: Token);

        var loaded = await database.Load<Cargo>(cargo.Id, Token);
        Assert.Equal(["A", "B"], loaded!.Parts.Select(p => p.Hold).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_sync_of_a_loaded_aggregate_inserts_updates_and_deletes_its_children_on_save()
    {
        await using var database = await StartDatabaseAsync();
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10), new(null, "B", 20)]);
        await cargo.Save(cancellationToken: Token);
        var stored = (await Cargo.Find(cargo.Id, Token))!;
        var (a, b) = (stored.Parts.Single(p => p.Hold == "A"), stored.Parts.Single(p => p.Hold == "B"));

        stored.Change([new(a.Id, "A", 12), new(null, "C", 3)]);
        await stored.Save(cancellationToken: Token);

        var parts = await database.Context.Set<CargoPart>().AsNoTracking().ToListAsync(Token);
        Assert.Equal(["A", "C"], parts.Select(p => p.Hold).Order(StringComparer.Ordinal));
        Assert.Equal(12, parts.Single(p => p.Id == a.Id).Tonnes);
        Assert.DoesNotContain(parts, p => p.Id == b.Id);
    }

    [Fact]
    public async Task Children_load_back_in_the_order_the_last_sync_gave_them_whatever_order_their_ids_sort_in()
    {
        await using var database = await StartDatabaseAsync();
        var cargo = Cargo.Load("Aurora", [new(null, "A", 1), new(null, "B", 2), new(null, "C", 3), new(null, "D", 4)]);
        var againstTheIds = cargo.Parts.OrderByDescending(static p => p.Id.ToString(), StringComparer.Ordinal).Select(static p => p.Hold).ToList();
        cargo.Change([.. againstTheIds.Select(hold => { var part = cargo.Parts.Single(p => p.Hold == hold); return new CargoPartSpec(part.Id, part.Hold, part.Tonnes); })]);

        await cargo.Save(cancellationToken: Token);
        var loaded = (await database.Load<Cargo>(cargo.Id, Token))!;
        var stored = (await Cargo.Find(cargo.Id, Token))!;
        stored.Change([.. stored.Parts.Reverse().Select(static p => new CargoPartSpec(p.Id, p.Hold, p.Tonnes))]);
        await stored.Save(cancellationToken: Token);
        var reversed = (await database.Load<Cargo>(cargo.Id, Token))!;

        Assert.Equal(againstTheIds, loaded.Parts.Select(static p => p.Hold));
        Assert.Equal(Enumerable.Reverse(againstTheIds), reversed.Parts.Select(static p => p.Hold));
    }

    [Fact]
    public async Task A_synced_childs_rule_is_refused_under_its_row_in_the_specs_order()
    {
        await using var database = await StartDatabaseAsync();
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10), new(null, "B", 0)]);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => cargo.Save(cancellationToken: Token));

        Assert.Equal("A part weighs something.", Assert.Single(refused.Errors["Parts[1].Tonnes"]));
    }

    [Fact]
    public void A_keyed_sync_matches_children_by_their_own_key_and_refuses_a_key_named_twice()
    {
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10), new(null, "B", 20)]);
        var (a, b) = (cargo.Parts[0], cargo.Parts[1]);

        var sync = cargo.Restow([new(null, "B", 25), new(null, "C", 5)]);
        var refused = Assert.Throws<ArgumentException>(() => cargo.Restow([new(null, "C", 1), new(null, "C", 2)]));

        Assert.Equal([b, sync.Added[0]], cargo.Parts);
        Assert.Equal([b], sync.Changed);
        Assert.Equal([a], sync.Removed);
        Assert.Equal(25, b.Tonnes);
        Assert.Equal("specs", refused.ParamName);
        Assert.Equal(5, cargo.Parts[1].Tonnes);
    }

    [Fact]
    public async Task A_child_the_aggregate_adds_or_removes_itself_is_inserted_or_deleted_on_save()
    {
        await using var database = await StartDatabaseAsync();
        var cargo = Cargo.Load("Aurora", [new(null, "A", 10)]);
        await cargo.Save(cancellationToken: Token);
        var stored = (await Cargo.Find(cargo.Id, Token))!;

        var stowed = stored.Stow("B", 7);
        var unloaded = stored.Unload(stored.Parts.Single(p => p.Hold == "A"));
        await stored.Save(cancellationToken: Token);

        Assert.True(unloaded);
        var parts = await database.Context.Set<CargoPart>().AsNoTracking().ToListAsync(Token);
        Assert.Equal(stowed.Id, Assert.Single(parts).Id);
    }

    [Fact]
    public async Task A_child_renamed_to_the_name_of_one_removed_in_the_same_save_is_written_after_the_delete()
    {
        await using var database = await StartDatabaseAsync();
        var barge = Barge.Named("Duna", "A", "B");
        await barge.Save(cancellationToken: Token);
        var stored = (await Barge.Find(barge.Id, Token))!;
        var b = stored.Holds.Single(h => h.Name == "B");

        stored.Change([new(b.Id, "A")]);
        var saved = await Record.ExceptionAsync(() => stored.Save(cancellationToken: Token));

        Assert.Null(saved);
        var holds = await database.Context.Set<BargeHold>().AsNoTracking().ToListAsync(Token);
        var only = Assert.Single(holds);
        Assert.Equal((b.Id, "A"), (only.Id, only.Name));
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
