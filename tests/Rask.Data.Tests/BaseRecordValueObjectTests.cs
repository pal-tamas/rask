using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary>
/// A value object declared from a base record, stored: one column for one value and complex columns for several,
/// the length and precision it declares, a stored value loaded as it is, and the rules a generated model asks.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class BaseRecordValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-vo-tanker-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Each_one_value_object_round_trips_as_one_column()
    {
        await using var database = await StartDatabaseAsync();
        var inspected = new DateOnly(2024, 5, 1);
        var lorry = Rig.Of("  Ada  ", "ABC-123", 120.5m, 3, chip: " C1 ", inspected: inspected);
        await lorry.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Rig>(lorry.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Ada", stored!.Driver.Value);
        Assert.Equal(" C1 ", stored.Chip!.Value);
        Assert.Equal("ABC-123", stored.Plate.Value);
        Assert.Equal(120.5m, stored.Tank.Value);
        Assert.Equal(3, stored.Axles.Value);
        Assert.Equal(inspected, stored.Inspected!.Value);
        var type = database.Context.Model.FindEntityType(typeof(Rig))!;
        Assert.Equal(typeof(string), type.FindProperty(nameof(Rig.Driver))!.GetValueConverter()!.ProviderClrType);
        Assert.DoesNotContain(type.GetComplexProperties(), p => p.Name == nameof(Rig.Driver));
    }

    [Fact]
    public async Task A_value_object_of_several_members_round_trips_as_complex_columns()
    {
        await using var database = await StartDatabaseAsync();
        var haul = Haul.Of("Ada", 10, 20);
        haul.Reopen(new DateOnly(2026, 3, 1), 7.5m);
        await haul.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Haul>(haul.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RigKm.Create(10, 20), stored!.Km);
        Assert.Equal(RigOpening.Create(new DateOnly(2026, 3, 1), 7.5m), stored.Opening);
        Assert.Contains(database.Context.Model.FindEntityType(typeof(Haul))!.GetComplexProperties(), p => p.Name == nameof(Haul.Km));
    }

    [Fact]
    public async Task The_column_takes_the_length_and_precision_the_value_object_declares()
    {
        await using var database = await StartDatabaseAsync();

        var type = database.Context.Model.FindEntityType(typeof(Rig))!;

        Assert.Equal(20, type.FindProperty(nameof(Rig.Driver))!.GetMaxLength());
        Assert.Equal(RigLimits.Chip, type.FindProperty(nameof(Rig.Chip))!.GetMaxLength());
        Assert.Null(type.FindProperty(nameof(Rig.Plate))!.GetMaxLength());
        Assert.Equal(9, type.FindProperty(nameof(Rig.Tank))!.GetPrecision());
        Assert.Equal(2, type.FindProperty(nameof(Rig.Tank))!.GetScale());
        Assert.Equal(new ValueFacets(MaxLength: 12), RigChip.__Facets);
    }

    [Fact]
    public async Task A_stored_value_its_rules_now_refuse_is_loaded_as_it_was_stored()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var lorry = Rig.Of("Ada", "ABC-123", 10, 3);
        await lorry.Save(cancellationToken: token);
        var haul = Haul.Of("Bea", 10, 20);
        await haul.Save(cancellationToken: token);
        var tooLong = new string('x', 30);
        await WritePastTheRulesAsync(database, typeof(Rig), "\"Driver\" = {0}, \"Plate\" = {1}", tooLong, "nonsense");
        await WritePastTheRulesAsync(database, typeof(Haul), "\"Driver\" = {0}, \"Km_End\" = {1}", " padded ", 5);

        var rig = await database.Load<Rig>(lorry.Id, token);
        var loaded = await database.Load<Haul>(haul.Id, token);
        var queried = await Rig.Where(r => r.Id == lorry.Id).First(token);

        Assert.Equal(tooLong, rig!.Driver.Value);
        Assert.Equal("nonsense", rig.Plate.Value);
        Assert.Equal((10, 5), (loaded!.Km.Start, loaded.Km.End));
        Assert.Equal(" padded ", loaded.Driver.Value);
        Assert.Equal(tooLong, queried.Driver.ToString());
        Assert.False(Loading.IsOn);
    }

    [Fact]
    public async Task A_tracked_edit_saves_while_another_stored_value_breaks_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var haul = Haul.Of("Ada", 10, 20);
        await haul.Save(cancellationToken: token);
        await WritePastTheRulesAsync(database, typeof(Haul), "\"Driver\" = {0}, \"Km_End\" = {1}", new string('x', 30), 5);
        await using var context = Db.CreateOwnContext();
        var tracked = await context.Set<Haul>().SingleAsync(h => h.Id == haul.Id, token);

        tracked.Reopen(new DateOnly(2026, 3, 1), 2m);
        await context.SaveChangesAsync(token);

        var stored = await database.Load<Haul>(haul.Id, token);
        Assert.Equal(2m, stored!.Opening.Quantity);
        Assert.Equal(5, stored.Km.End);
        Assert.Equal(30, stored.Driver.Value.Length);
    }

    [Fact]
    public async Task A_failed_materialization_leaves_the_next_Create_checking_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var row = Sealed.Of(1);
        await row.Save(cancellationToken: token);
        await WritePastTheRulesAsync(database, typeof(Sealed), "\"Seal_Count\" = {0}", -1);
        var raw = Record.Exception(() => database.Context.Set<Sealed>().AsNoTracking().ToList());
        var afterTheRawQuery = Loading.IsOn;

        var read = await Record.ExceptionAsync(() => database.Load<Sealed>(row.Id, token));

        Assert.IsType<InvalidOperationException>(raw);
        Assert.IsType<InvalidOperationException>(read);
        Assert.False(afterTheRawQuery);
        Assert.False(Loading.IsOn);
        Assert.Throws<RaskValidationException>(() => RigKm.Create(5, 1));
    }

    [Fact]
    public async Task A_range_the_generated_write_refuses_is_refused_under_the_member_that_holds_it()
    {
        await using var database = await StartDatabaseAsync();
        var backwards = new LegModel { Km = new() { Start = 20, End = 10 } };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Leg.Create(backwards, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["Km.End"], refused.Failures.Select(f => f.Fields.Single()));
    }

    [Fact]
    public async Task The_generated_writes_refuse_a_value_its_rules_refuse_under_its_field()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var valid = new RigModel { Driver = "Ada", Plate = "ABC-123", Tank = 50, Axles = 3, Chip = "" };

        var created = await Rig.Create(valid, cancellationToken: token);
        var create = await Assert.ThrowsAsync<RaskValidationException>(
            () => Rig.Create(new RigModel { Driver = new string('x', 21), Plate = "ABC-123", Tank = 50, Axles = 3 }, cancellationToken: token));
        var update = await Assert.ThrowsAsync<RaskValidationException>(
            () => Rig.Update(created.Id, new RigModel { Driver = "Ada", Plate = "nope", Tank = 50, Axles = 3 }, cancellationToken: token));

        Assert.Null(created.Chip);
        Assert.Equal([("Driver", "Enter at most 20 characters.")], create.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal([("Plate", RigPlate.Form)], update.Failures.Select(f => (f.Fields.Single(), f.Message)));
    }

    [Fact]
    public void A_form_bound_to_the_generated_model_asks_the_value_objects_rule_under_its_field()
    {
        var ruleOf = RuleOfRigModel();

        var driver = (Rask.Core.Forms.Validate<string?>)ruleOf("Driver")!;
        var chip = (Rask.Core.Forms.Validate<string?>)ruleOf("Chip")!;
        var axles = (Rask.Core.Forms.Validate<long?>)ruleOf("Axles")!;

        Assert.Equal(["Enter at most 20 characters."], driver(new string('x', 21)));
        Assert.Equal([GeneratedModelRules.RequiredMessage], driver(null));
        Assert.Empty(driver("Ada"));
        Assert.Empty(chip(""));
        Assert.Equal(["Enter at most 12 characters."], chip(new string('x', 13)));
        Assert.Equal(["Enter 2 or more."], axles(1));
    }

    [Fact]
    public void A_form_bound_to_the_generated_model_is_told_each_text_fields_most_characters()
    {
        var maxLengthOf = Generated<Func<string, int?>>("Root_MaxLengthOf");

        var driver = maxLengthOf("Driver");
        var chip = maxLengthOf("Chip");
        var plate = maxLengthOf("Plate");
        var axles = maxLengthOf("Axles");

        Assert.Equal(20, driver);
        Assert.Equal(12, chip);
        Assert.Null(plate);
        Assert.Null(axles);
    }

    // Written past the rules on purpose: a row stored before a rule was tightened. The table name comes from the
    // model, the values are parameters.
    private static async Task WritePastTheRulesAsync(TestDatabase database, Type entity, string set, params object[] values)
    {
        var table = database.Context.Model.FindEntityType(entity)!.GetTableName();
#pragma warning disable EF1002
        await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET {set}", values, TestContext.Current.CancellationToken);
#pragma warning restore EF1002
    }

    // The generated switch a form asks, reached the only way a file-local class can be: by its name.
    private static Func<string, Delegate?> RuleOfRigModel() => Generated<Func<string, Delegate?>>("Root_RuleOf");

    private static T Generated<T>(string name)
        where T : Delegate
    {
        var rules = typeof(RigModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__RigModelRules", StringComparison.Ordinal));
        var method = rules.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return method.CreateDelegate<T>();
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
