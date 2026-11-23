namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// The field shapes a control bound to a generated model is told: each value object's <c>__Field</c>, read from its
/// base call, and the model's <c>RaskValidation.RegisterFieldShapes</c> — value objects, a nested range's ends, an
/// enum's members with their labels, another aggregate's typed id — and a range's rule asked live under each end.
/// </summary>
public class FieldShapeGeneratorTests
{
    private const string Kind = "global::Rask.Core.Forms.FieldKind";

    private const string Source = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using Rask.Data;
        using Range = Rask.Data.Range;
        namespace Shop;

        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100, MinLength: 2);
        public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Max: 999.5m, Scale: 2);
        public sealed partial record Pieces(long Value) : Whole(Value, Min: 0, Max: 1000);
        public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);
        public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true, Min: 0, Max: 9999);
        public sealed partial record Shift(DateTime From, DateTime? To) : Range(From, To);
        public readonly record struct DepotId(Guid Value);
        public enum Fuel { Diesel, [Display(Name = "Petrol (95)")] Petrol }

        public sealed class Depot : Aggregate<DepotId, Model>
        {
            private Depot() { }
            public string Name { get; private set; } = "";
        }

        public sealed class Trip : Aggregate<Guid, Model>
        {
            private Trip() { }
            public DriverName Driver { get; private set; } = DriverName.Create("Ada");
            public Litres Fuelled { get; private set; } = Litres.Create(1);
            public Pieces Count { get; private set; } = Pieces.Create(0);
            public InvoiceDay? Invoiced { get; private set; }
            public KmRange Km { get; private set; } = KmRange.Create(0, 1);
            public Shift? Worked { get; private set; }
            public Fuel Fuel { get; private set; }
            public DepotId Depot { get; private set; }
            public string Note { get; private set; } = "";
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"]);

    [Theory]
    [InlineData("DriverName", Kind + ".Text, MaxLength: 100, MinLength: 2")]
    [InlineData("Litres", Kind + ".Number, Min: 1m, Max: 999.5m, Scale: 2")]
    [InlineData("Pieces", Kind + ".Whole, Min: 0m, Max: 1000m")]
    [InlineData("InvoiceDay", Kind + ".Day, NotAfterToday: true")]
    [InlineData("KmRange", Kind + ".Range, Min: 0m, Max: 9999m, Strict: true, Start: \"Start\", End: \"End\", Ends: " + Kind + ".Whole")]
    [InlineData("Shift", Kind + ".Range, Start: \"From\", End: \"To\")")]
    public void Each_value_object_tells_its_field_the_rules_its_base_call_states(string name, string shape)
    {
        var run = Run();

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::Rask.Core.Forms.FieldShape __Field => new(" + shape, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_registers_a_shape_for_each_field_its_type_cannot_tell_alone()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Contains("RegisterFieldShapes(typeof(global::Shop.TripModel), Root_ShapeOf);", model, StringComparison.Ordinal);
        Assert.Contains("\"Driver\" => global::Shop.DriverName.__Field,", model, StringComparison.Ordinal);
        Assert.Contains("\"Km\" => global::Shop.KmRange.__Field,", model, StringComparison.Ordinal);
        Assert.Contains("\"Depot\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Id),", model, StringComparison.Ordinal);
        Assert.Contains(
            "\"Fuel\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Choice, Options: new global::Rask.Core.Forms.FieldOption[] { "
            + "new global::Rask.Core.Forms.FieldOption(global::Shop.Fuel.Diesel, \"Diesel\"), "
            + "new global::Rask.Core.Forms.FieldOption(global::Shop.Fuel.Petrol, \"Petrol (95)\") }),",
            model, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Note\" => new global::Rask.Core.Forms.FieldShape", model, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Note\" => global::", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nested_range_model_registers_its_ends_of_the_ends_kind()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Contains("RegisterFieldShapes(typeof(global::Shop.TripModel.KmRangeModel), KmRangeModel_ShapeOf);", model, StringComparison.Ordinal);
        Assert.Contains("\"Start\" => __EndsOf(global::Shop.KmRange.__Field),", model, StringComparison.Ordinal);
        Assert.Contains("\"End\" => __EndsOf(global::Shop.KmRange.__Field),", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_the_aggregate_always_holds_is_on_a_new_model_from_the_start()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Contains("public KmRangeModel? Km { get; set; } = new();", model, StringComparison.Ordinal);
        Assert.Contains("public ShiftModel? Worked { get; set; }\n", model.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("?? new global::Shop.TripModel.KmRangeModel();", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_end_of_a_nested_range_asks_the_ranges_own_rule_beside_the_other_end()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "RegisterFieldRules(typeof(global::Shop.TripModel.KmRangeModel), KmRangeModel_RuleOf);", model, StringComparison.Ordinal);
        Assert.Contains("global::System.Func<object, int?, global::System.Collections.Generic.IEnumerable<string>> KmRangeModel_End =", model, StringComparison.Ordinal);
        Assert.Contains("__InRange_KmRangeModel(model.Start, value, \"End\")", model, StringComparison.Ordinal);
        Assert.Contains("__InRange_KmRangeModel(value, model.End, \"Start\")", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.KmRange.TryCreate(__start, __end, out _, out var __problems);", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Shift.TryCreate(__start, end, out _, out var __problems);", model, StringComparison.Ordinal);
    }
}
