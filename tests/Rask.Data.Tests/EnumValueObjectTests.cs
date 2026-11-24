using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Forms;

namespace Rask.Data.Tests;

/// <summary>What a pump pours.</summary>
public enum PumpFuel
{
    Diesel,

    [Display(Name = "AdBlue")]
    Urea,

    Petrol,
}

/// <summary>An enum held as a value object: one member over <see cref="Value" />, with a rule of its own.</summary>
public sealed partial record PumpFuelKind(PumpFuel Value) : Value
{
    public const string Unknown = "Choose a fuel the pump knows.";

    protected override IEnumerable<Problem> Rules()
    {
        if (!Enum.IsDefined(Value))
        {
            yield return Problem.Whole(Unknown);
        }
    }
}

/// <summary>A pump that pours one fuel, written through its generated model.</summary>
public sealed class Pump : Aggregate<int, Model>
{
    private Pump() { }

    public PumpFuelKind Fuel { get; private set; } = PumpFuelKind.Create(PumpFuel.Diesel);

    public PumpFuelKind? Spare { get; private set; }

    public static Pump Pouring(PumpFuel fuel, PumpFuel? spare = null) =>
        new() { Fuel = PumpFuelKind.Create(fuel, nameof(Fuel)), Spare = PumpFuelKind.CreateOptional(spare, nameof(Spare)) };
}

/// <summary>
/// An enum as a value object is one member over <see cref="Value" />: one column of the enum's own type, queried by
/// its value, carried on the generated model as the enum itself and offered to a control as a choice of its members.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class EnumValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-enum-vo-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task An_enum_value_object_is_one_column_of_the_enum_and_round_trips()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var pump = Pump.Pouring(PumpFuel.Urea, spare: PumpFuel.Petrol);
        await pump.Save(cancellationToken: token);

        var stored = await database.Load<Pump>(pump.Id, token);
        var urea = await database.Context.Set<Pump>().CountAsync(p => p.Fuel.Value == PumpFuel.Urea, token);

        Assert.Equal((PumpFuel.Urea, PumpFuel.Petrol), (stored!.Fuel.Value, stored.Spare!.Value));
        Assert.Equal(1, urea);
        var column = database.Context.Model.FindEntityType(typeof(Pump))!.FindProperty(nameof(Pump.Fuel))!;
        Assert.Equal(typeof(PumpFuel), column.GetValueConverter()!.ProviderClrType);
        Assert.DoesNotContain(database.Context.Model.FindEntityType(typeof(Pump))!.GetComplexProperties(), p => p.Name == nameof(Pump.Fuel));
    }

    [Fact]
    public void The_generated_model_carries_the_enum_and_its_rule()
    {
        var fuel = typeof(PumpModel).GetProperty(nameof(PumpModel.Fuel))!.PropertyType;
        var refused = PumpFuelKind.TryCreate((PumpFuel)42, out _, out var problems);

        Assert.Equal(typeof(PumpFuel?), fuel);
        Assert.False(refused);
        Assert.Equal([PumpFuelKind.Unknown], problems.Select(p => p.Message));
    }

    [Fact]
    public void A_control_bound_to_an_enum_value_object_is_offered_its_members()
    {
        var shapeOf = Generated<Func<string, FieldShape?>>("Root_ShapeOf");

        var fuel = shapeOf(nameof(PumpModel.Fuel));
        var spare = shapeOf(nameof(PumpModel.Spare));

        Assert.Equal(FieldKind.Choice, fuel!.Kind);
        Assert.Equal([(PumpFuel.Diesel, "Diesel"), (PumpFuel.Urea, "AdBlue"), (PumpFuel.Petrol, "Petrol")],
            fuel.Options!.Select(o => ((PumpFuel)o.Value, o.Label)));
        Assert.Equal(FieldKind.Choice, spare!.Kind);
    }

    [Fact]
    public async Task A_pump_is_written_from_its_generated_model()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;

        var id = (await Pump.Create(new PumpModel { Fuel = PumpFuel.Petrol }, cancellationToken: token)).Id;

        var stored = await database.Load<Pump>(id, token);
        Assert.Equal(PumpFuel.Petrol, stored!.Fuel.Value);
        Assert.Null(stored.Spare);
    }

    private static T Generated<T>(string name)
        where T : Delegate
    {
        var rules = typeof(PumpModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__PumpModelRules", StringComparison.Ordinal));
        var method = rules.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return method.CreateDelegate<T>();
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
