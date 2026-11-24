using Microsoft.EntityFrameworkCore;
using Rask.Core.Forms;
using Rask.Wire;

namespace Rask.Data.Tests;

// The generated Form.For of an operation that takes rows for its Children<,> and value objects of several values: read
// from what is stored, each row and each member a field, refused under its place, and saved through the entry.
[Collection(DataDbCollection.Name)]
public sealed class OperationFormEntryTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-operation-forms-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 24, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_row_list_is_read_a_row_per_child_and_saved_as_the_children_it_now_lists()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync(new ConvoyTruckSpec(null, "AAA-111", ConvoyLoad.Create(10)), new ConvoyTruckSpec(null, "BBB-222", ConvoyLoad.Create(20)));
        var form = Form.For(Convoy.Line, convoy.Id);

        // The rows come back in the order the children are loaded in, which the store does not promise: read by plate.
        var input = (await form.Read(Token))!;
        var read = input.Trucks.Select(static t => (t.Plate, t.Load)).Order().ToList();
        input.Trucks.RemoveAll(static t => t.Plate == "AAA-111");
        input.Trucks.Single(static t => t.Plate == "BBB-222").Load = 25;
        input.Trucks.Add(new() { Plate = "CCC-333", Load = 5 });
        await form.Submit(input, Token);
        var stored = (await database.Load<Convoy>(convoy.Id, Token))!;

        Assert.Equal([("AAA-111", 10m), ("BBB-222", 20m)], read);
        Assert.Equal([("BBB-222", 25m), ("CCC-333", 5m)], stored.Trucks.Select(static t => (t.Plate, t.Load)).Order());
        Assert.Equal(convoy.Trucks.Single(static t => t.Plate == "BBB-222").Id, stored.Trucks.Single(static t => t.Plate == "BBB-222").Id);
    }

    [Fact]
    public async Task A_rows_field_left_empty_is_required_under_its_place()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync(new ConvoyTruckSpec(null, "AAA-111", ConvoyLoad.Create(10)));
        var input = (await Form.For(Convoy.Line, convoy.Id).Read(Token))!;

        input.Trucks.Add(new() { Plate = "CCC-333" });
        input.Trucks[0].Load = 0.5m;
        var failures = await StoreRules<ConvoyLineInput>(input);

        Assert.Contains("Trucks[0].Load", Fields(failures));
        Assert.Contains("Trucks[1].Load", Fields(failures));
    }

    [Fact]
    public async Task What_the_operation_and_the_childs_own_rules_refuse_is_told_under_the_row()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync(new ConvoyTruckSpec(null, "AAA-111", ConvoyLoad.Create(10)));
        var input = (await Form.For(Convoy.Line, convoy.Id).Read(Token))!;

        input.Trucks.Add(new() { Plate = Convoy.Banned, Load = 3 });
        var banned = await StoreRules<ConvoyLineInput>(input);
        input.Trucks[1].Plate = "LONGER-THAN-8";
        var tooLong = await StoreRules<ConvoyLineInput>(input);

        Assert.Equal(["Trucks[1].Plate"], Fields(banned));
        Assert.Equal(["Trucks[1].Plate"], Fields(tooLong));
    }

    [Fact]
    public async Task A_value_object_of_several_values_is_read_into_its_group_and_refused_under_its_member()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync();
        await Convoy.Open(convoy.Id, 0, RigOpening.Create(new DateOnly(2026, 3, 1), 7.5m), Token);
        var form = Form.For(Convoy.Open, convoy.Id);

        var input = (await form.Read(Token))!;
        var read = (input.Opening.Date, input.Opening.Quantity);
        input.Opening.Date = null;
        var refused = await StoreRules<ConvoyOpenInput>(input);

        Assert.Equal((new DateOnly(2026, 3, 1), 7.5m), read);
        Assert.Equal(["Opening.Quantity"], Fields(refused));
    }

    [Fact]
    public async Task A_value_object_that_may_be_absent_is_cleared_by_leaving_its_group_empty()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync();
        await Convoy.Plan(convoy.Id, 0, ConvoyWindow.Create(new DateOnly(2026, 3, 1), 3), Token);
        var form = Form.For(Convoy.Plan, convoy.Id);

        var input = (await form.Read(Token))!;
        var read = (input.Window.From, input.Window.Days);
        input.Window.From = null;
        input.Window.Days = null;
        await form.Submit(input, Token);

        Assert.Equal((new DateOnly(2026, 3, 1), 3), read);
        Assert.Null((await database.Load<Convoy>(convoy.Id, Token))!.Window);
    }

    [Fact]
    public async Task A_value_object_that_may_be_absent_but_is_partly_given_says_what_it_lacks_and_what_its_rules_refuse()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync();
        var input = (await Form.For(Convoy.Plan, convoy.Id).Read(Token))!;

        input.Window.Days = 0;
        var lacking = await StoreRules<ConvoyPlanInput>(input);
        input.Window.From = new DateOnly(2026, 3, 1);
        var refused = await StoreRules<ConvoyPlanInput>(input);

        Assert.Equal(["Window.From"], Fields(lacking));
        Assert.Equal(["Window.Days"], Fields(refused));
    }

    [Fact]
    public async Task A_range_is_refused_under_its_end_and_a_missing_end_as_required()
    {
        await using var database = await StartDatabaseAsync();
        var convoy = await StoredAsync();
        var input = (await Form.For(Convoy.Measure, convoy.Id).Read(Token))!;

        input.Km.Start = 20;
        var backwards = await StoreRules<ConvoyMeasureInput>(input);
        input.Km.End = null;
        var missing = await StoreRules<ConvoyMeasureInput>(input);

        Assert.Equal(["Km.End"], Fields(backwards));
        Assert.Equal(["Km.End"], Fields(missing));
        Assert.Equal(Rules.Required, Assert.Single(missing).Message);
    }

    private static async Task<IReadOnlyList<FieldFailure>> StoreRules<TInput>(TInput input) where TInput : class =>
        await RaskValidation.ResolveStoreRules(typeof(TInput), null)!.Check(input, null, Token);

    private static List<string> Fields(IReadOnlyList<FieldFailure> failures) => [.. failures.SelectMany(static f => f.Fields)];

    private static async Task<Convoy> StoredAsync(params ConvoyTruckSpec[] trucks)
    {
        var convoy = Convoy.Assembled("North", trucks);
        await convoy.Save(cancellationToken: Token);
        return convoy;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
