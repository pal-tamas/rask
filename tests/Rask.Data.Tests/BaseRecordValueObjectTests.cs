using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary>A text with a length, trimmed.</summary>
public sealed partial record RigDriver() : Text(MaxLength: 20);

/// <summary>A text kept as typed.</summary>
public sealed partial record RigChip() : Text(MaxLength: 12, Trim: false);

/// <summary>A text with a pattern and its own words for it.</summary>
public sealed partial record RigPlate() : Text(Pattern: @"^[A-Z]{3}-\d{3}$", Message: RigPlate.Form)
{
    public const string Form = "A plate is three letters and three digits.";
}

/// <summary>A decimal with a least and a scale.</summary>
public sealed partial record RigLitres() : Number(Min: 1, Scale: 2, Precision: 9);

/// <summary>A whole number in a range.</summary>
public sealed partial record RigAxles() : Whole(Min: 2, Max: 9);

/// <summary>A day that cannot be in the future.</summary>
public sealed partial record RigInspected() : Day(NotAfterToday: true);

/// <summary>An aggregate holding one value object of each base.</summary>
public sealed class Rig : Aggregate<int, Model, Deletable>
{
    private Rig() { }

    public RigDriver Driver { get; private set; } = RigDriver.Create("Nobody");

    public RigChip? Chip { get; private set; }

    public RigPlate Plate { get; private set; } = RigPlate.Create("AAA-000");

    public RigLitres Tank { get; private set; } = RigLitres.Create(1);

    public RigAxles Axles { get; private set; } = RigAxles.Create(2);

    public RigInspected? Inspected { get; private set; }

    public static Rig Of(string driver, string plate, decimal tank, long axles, string? chip = null, DateOnly? inspected = null) =>
        new()
        {
            Driver = RigDriver.Create(driver, nameof(Driver)),
            Plate = RigPlate.Create(plate, nameof(Plate)),
            Tank = RigLitres.Create(tank, nameof(Tank)),
            Axles = RigAxles.Create(axles, nameof(Axles)),
            Chip = RigChip.CreateOptional(chip, nameof(Chip)),
            Inspected = RigInspected.CreateOptional(inspected, nameof(Inspected)),
        };
}

/// <summary>
/// A value object declared from a base record: one column, the length and precision it declares, rules asked by
/// <c>Create</c> and by the generated writes, and a stored value loaded as it is.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class BaseRecordValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-vo-tanker-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Each_value_object_round_trips_as_one_column()
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
    public async Task The_column_takes_the_length_and_precision_the_value_object_declares()
    {
        await using var database = await StartDatabaseAsync();
        var type = database.Context.Model.FindEntityType(typeof(Rig))!;

        Assert.Equal(20, type.FindProperty(nameof(Rig.Driver))!.GetMaxLength());
        Assert.Equal(12, type.FindProperty(nameof(Rig.Chip))!.GetMaxLength());
        Assert.Null(type.FindProperty(nameof(Rig.Plate))!.GetMaxLength());
        Assert.Equal(9, type.FindProperty(nameof(Rig.Tank))!.GetPrecision());
        Assert.Equal(2, type.FindProperty(nameof(Rig.Tank))!.GetScale());
    }

    [Fact]
    public async Task A_stored_value_is_loaded_as_it_is_without_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var lorry = Rig.Of("Ada", "ABC-123", 10, 3);
        await lorry.Save(cancellationToken: TestContext.Current.CancellationToken);
        var table = database.Context.Model.FindEntityType(typeof(Rig))!.GetTableName();
        var tooLong = new string('x', 30);

#pragma warning disable EF1002 // the table name comes from the model, the values are parameters
        await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET \"Driver\" = {{0}}, \"Plate\" = {{1}}", [tooLong, "nonsense"], TestContext.Current.CancellationToken);
#pragma warning restore EF1002

        var stored = await database.Load<Rig>(lorry.Id, TestContext.Current.CancellationToken);

        Assert.Equal(tooLong, stored!.Driver.Value);
        Assert.Equal("nonsense", stored.Plate.Value);
    }

    [Fact]
    public void Two_values_are_equal_by_type_and_value_only()
    {
        Assert.Equal(RigDriver.Create(" Ada "), RigDriver.Create("Ada"));
        Assert.Equal(RigDriver.Create("Ada").GetHashCode(), RigDriver.Create("Ada ").GetHashCode());
        Assert.NotEqual(RigDriver.Create("Ada"), RigDriver.Create("Bea"));
        Assert.False(RigDriver.Create("Ada").Equals(RigChip.Create("Ada")));
        Assert.Equal("Ada", RigDriver.Create("Ada").ToString());
    }

    [Fact]
    public void Create_refuses_under_the_field_it_is_given()
    {
        var refused = Assert.Throws<RaskValidationException>(() => RigDriver.Create(new string('x', 21), "Driver"));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["Driver"], failure.Fields);
        Assert.Equal("Enter at most 20 characters.", failure.Message);
    }

    [Fact]
    public void Each_base_says_which_rule_was_broken()
    {
        Assert.Equal([GeneratedModelRules.RequiredMessage], RigDriver.__Problems("   "));
        Assert.Equal([RigPlate.Form], RigPlate.__Problems("abc"));
        Assert.Equal(["Enter 1 or more.", "Enter at most 2 decimal places."], RigLitres.__Problems(0.125m));
        Assert.Equal(["The number is out of range."], RigLitres.__Problems(10_000_000m));
        Assert.Equal(["Enter 9 or less."], RigAxles.__Problems(10));
        Assert.Single(RigInspected.__Problems(Clock.Today.AddDays(1)));
        Assert.Empty(RigInspected.__Problems(Clock.Today));
        Assert.Empty(RigChip.__Problems(" 123456789A "));
        Assert.Single(RigChip.__Problems(" 123456789AB "));
    }

    [Fact]
    public void TryCreate_and_CreateOptional_answer_without_throwing()
    {
        Assert.False(RigAxles.TryCreate(1, out var none, out var problems));
        Assert.Null(none);
        Assert.Equal(["Enter 2 or more."], problems);

        Assert.True(RigAxles.TryCreate(4, out var four, out _));
        Assert.Equal(4, four.Value);

        Assert.Null(RigChip.CreateOptional("  "));
        Assert.Null(RigInspected.CreateOptional(null));
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

    [Fact]
    public async Task A_stored_value_its_rules_now_refuse_is_loaded_as_it_was_stored()
    {
        await using var database = await StartDatabaseAsync();
        var lorry = Rig.Of("Ada", "ABC-123", 50, 3);
        await lorry.Save(cancellationToken: TestContext.Current.CancellationToken);
        await using (var db = Db.CreateOwnContext())
        {
            // Written past the rules on purpose: a row stored before the rule was tightened.
            var update = "UPDATE \"" + db.Model.FindEntityType(typeof(Rig))!.GetTableName() + "\" SET \"Driver\" = {0} WHERE \"Id\" = {1}";
            await db.Database.ExecuteSqlRawAsync(update, [new string('x', 30), lorry.Id], TestContext.Current.CancellationToken);
        }

        var stored = await database.Load<Rig>(lorry.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new string('x', 30), stored!.Driver.Value);
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
