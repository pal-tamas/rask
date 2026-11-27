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
        using Rask.Values;
        using Range = Rask.Values.Range;
        namespace Shop;

        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100, MinLength: 2);
        public sealed partial record Plate(string Value) : Text(Value, MaxLength: 20, Upper: true);
        public sealed partial record PostCode(string Value) : Text(Value, Pattern: @"^\d{4}$");
        public sealed partial record Grade(string Value) : Text(Value, Pattern: "^[A-F]$", Upper: true);
        public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Max: 999.5m, Scale: 2);
        public sealed partial record Pieces(long Value) : Integer(Value, Min: 0, Max: 1000);
        public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);
        public sealed partial record FuelingMoment(DateTime Value) : Moment(Value, NotAfterNow: true);
        public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true, Min: 0, Max: 9999);
        public sealed partial record Shift(DateTime From, DateTime? To) : Range(From, To);
        public readonly record struct DepotId(Guid Value);
        public enum Fuel { Diesel, [Display(Name = "Petrol (95)")] Petrol }

        public sealed class Depot : Aggregate<DepotId>, IHasForm
        {
            private Depot() { }
            public string Name { get; private set; } = "";
        }

        public sealed class Trip : Aggregate<Guid>, IHasForm
        {
            private Trip() { }
            public DriverName Driver { get; private set; } = DriverName.Create("Ada");
            public Litres Fuelled { get; private set; } = Litres.Create(1);
            public Pieces Count { get; private set; } = Pieces.Create(0);
            public InvoiceDay? Invoiced { get; private set; }
            public FuelingMoment? FuelledAt { get; private set; }
            public KmRange Km { get; private set; } = KmRange.Create(0, 1);
            public Shift? Worked { get; private set; }
            public Fuel Fuel { get; private set; }
            public DepotId Depot { get; private set; }
            public string Note { get; private set; } = "";
            [EmailAddress, StringLength(80, MinimumLength = 5)] public string Contact { get; private set; } = "";
            [Editable(false), UIHint("Switch")] public bool Locked { get; private set; }
            [MaxLength(50)] public DriverName Second { get; private set; } = DriverName.Create("Bea");
            [DisplayFormat(ConvertEmptyStringToNull = false), DataType(DataType.MultilineText)] public string? Remark { get; private set; }
            [DataType(DataType.Date)] public DateTime Started { get; private set; }
            [Phone, Editable(false)] public Fuel Spare { get; private set; }
            public Plate Plate { get; private set; } = Plate.Create("ABC123");
            public PostCode Post { get; private set; } = PostCode.Create("1234");
            [RegularExpression("^[A-Z]{2}$")] public string Country { get; private set; } = "HU";
            [Display(Name = "Distance"), DisplayFormat(DataFormatString = "{0:N1}", NullDisplayText = "n/a"), ScaffoldColumn(false)]
            public decimal? Distance { get; private set; }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            Source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"]);

    private static GeneratorRun RunWithReadFaces() =>
        GeneratorHarness.Run(
            Source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator(), new ReadModelGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"]);

    [Theory]
    [InlineData("DriverName", Kind + ".Text, MaxLength: 100, MinLength: 2")]
    [InlineData("Plate", Kind + ".Text, MaxLength: 20) { Upper = true });")]
    [InlineData("Litres", Kind + ".Number, Min: 1m, Max: 999.5m, Scale: 2")]
    [InlineData("Pieces", Kind + ".Integer, Min: 0m, Max: 1000m")]
    [InlineData("InvoiceDay", Kind + ".Day, NotAfterToday: true")]
    [InlineData("FuelingMoment", Kind + ".Moment, NotAfterNow: true")]
    [InlineData("KmRange", Kind + ".Range, Min: 0m, Max: 9999m, Strict: true, Start: \"Start\", End: \"End\", Ends: " + Kind + ".Integer")]
    [InlineData("Shift", Kind + ".Range, Start: \"From\", End: \"To\")")]
    public void Each_value_object_tells_its_field_the_rules_its_base_call_states(string name, string shape)
    {
        var run = Run();

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"Value\", new global::Rask.Core.Forms.FieldShape(" + shape, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_objects_pattern_is_told_as_one_the_whole_trimmed_text_matches()
    {
        var run = Run();

        var post = run.GeneratedSource("PostCode.Value.g.cs");
        var grade = run.GeneratedSource("Grade.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "new global::Rask.Core.Forms.FieldShape(" + Kind + ".Text) { Pattern = global::Rask.Core.Forms.HtmlPattern.Found(\"^\\\\d{4}$\", trimmed: true) });",
            post, StringComparison.Ordinal);
        Assert.DoesNotContain("Pattern =", grade, StringComparison.Ordinal);
    }

    [Fact]
    public void A_regular_expression_attribute_registers_its_pattern()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "\"Country\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Text) { Pattern = \"^[A-Z]{2}$\" },", model, StringComparison.Ordinal);
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
        Assert.Contains("__InValue_KmRangeModel(model.Start, value, \"End\")", model, StringComparison.Ordinal);
        Assert.Contains("__InValue_KmRangeModel(value, model.End, \"Start\")", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.KmRange.TryCreate(__Start, __End, out _, out var __problems);", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Shift.TryCreate(__From, __ToGiven, out _, out var __problems);", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_property_registers_what_its_attributes_tell_its_field()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "\"Contact\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Text, MaxLength: 80, MinLength: 5) { Input = global::Rask.Core.InputType.Email },",
            model, StringComparison.Ordinal);
        Assert.Contains(
            "\"Locked\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Text) { ReadOnly = true, Hint = \"Switch\" },", model, StringComparison.Ordinal);
        Assert.Contains(
            "\"Remark\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Text) { KeepEmpty = true, Hint = \"MultilineText\" },", model, StringComparison.Ordinal);
        Assert.Contains(
            "\"Started\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Moment) { Input = global::Rask.Core.InputType.Date },", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_objects_length_is_tightened_by_its_propertys_attribute_and_its_hints_are_added()
    {
        var run = Run();

        var model = run.GeneratedSource("TripModel.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("\"Second\" => __Within(global::Shop.DriverName.__Field, 50, null),", model, StringComparison.Ordinal);
        Assert.Contains("\"Spare\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Choice, Options:", model, StringComparison.Ordinal);
        Assert.Contains(" with { Input = global::Rask.Core.InputType.Tel, ReadOnly = true },", model, StringComparison.Ordinal);
        Assert.Contains("private static global::Rask.Core.Forms.FieldShape __Within(", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_face_carries_how_a_list_shows_each_member_and_registers_its_shape()
    {
        var run = RunWithReadFaces();

        var face = run.GeneratedSource("Shop_TripRead.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "[global::System.ComponentModel.DataAnnotations.DisplayAttribute(Name = \"Distance\")]"
            + "[global::System.ComponentModel.DataAnnotations.DisplayFormatAttribute(DataFormatString = \"{0:N1}\", NullDisplayText = \"n/a\")]"
            + "[global::System.ComponentModel.DataAnnotations.ScaffoldColumnAttribute(false)]",
            face, StringComparison.Ordinal);
        Assert.Contains("RegisterFieldShapes(typeof(global::Shop.TripRead), ShapeOf);", face, StringComparison.Ordinal);
        Assert.Contains("\"Driver\" => global::Shop.DriverName.__Field,", face, StringComparison.Ordinal);
        Assert.Contains("\"Second\" => __Within(global::Shop.DriverName.__Field, 50, null),", face, StringComparison.Ordinal);
        Assert.Contains(
            "\"Distance\" => new global::Rask.Core.Forms.FieldShape(" + Kind + ".Number) { Format = \"{0:N1}\", NullText = \"n/a\", Listed = false },",
            face, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Note\" =>", face, StringComparison.Ordinal);
    }
}
