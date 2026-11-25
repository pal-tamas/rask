using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A value object declared from a Rask base record — <c>record KmRange(int Start, int End) : Range(Start, End)</c>:
/// its generated half, how a generated model builds and asks it, the column facets the registry hands EF Core, and
/// RASK127–130.
/// </summary>
public class ValueObjectFactoryGeneratorTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        using Rask.Values;
        using Range = Rask.Values.Range;
        namespace Shop;

        """;

    private const string ValueObjects = """
        public static class Limits { public const int Name = 1000; }
        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: Limits.Name);
        public sealed partial record ChipCode(string Value) : Text(Value, MaxLength: 1000, Trim: false);
        public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);
        public sealed partial record Pieces(long Value) : Integer(Value, Min: 0, Max: 1000);
        public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);
        public sealed partial record FuelingMoment(DateTime Value) : Moment(Value);
        public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);
        public sealed partial record ChipPeriod(DateTime From, DateTime? To) : Range(From, To);
        public sealed partial record Opening(DateOnly? Date, decimal? Quantity) : Value
        {
            protected override IEnumerable<Problem> Rules()
            {
                if (Date is null != Quantity is null) yield return Problem.At(Quantity, "Both or neither.");
            }
        }

        """;

    private const string Holder = """
        public sealed class Delivery : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
        {
            private Delivery() { }
            public DriverName Driver { get; private set; } = DriverName.Create("x");
            public DriverName? Mate { get; private set; }
            public Litres Fuel { get; private set; } = Litres.Create(1);
            public Pieces Count { get; private set; } = Pieces.Create(0);
            public InvoiceDay? Invoiced { get; private set; }
            public FuelingMoment At { get; private set; } = FuelingMoment.Create(new DateTime(2026, 1, 1));
        }
        """;

    private static GeneratorRun Run(string source, params string[] extra) =>
        GeneratorHarness.Run(
            Usings + source,
            [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore", .. extra]);

    [Theory]
    [InlineData("DriverName")]
    [InlineData("Litres")]
    [InlineData("Pieces")]
    [InlineData("InvoiceDay")]
    [InlineData("FuelingMoment")]
    public void A_one_value_record_over_an_ordered_value_orders_like_it(string name)
    {
        var run = Run(ValueObjects);

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains($"partial record {name} : global::System.IComparable<{name}>, global::System.IComparable", generated, StringComparison.Ordinal);
        Assert.Contains($"public static bool operator >=({name}? left, {name}? right)", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("KmRange")]
    [InlineData("Opening")]
    [InlineData("Sorted")]
    [InlineData("Unordered")]
    public void A_record_of_several_values_its_own_order_or_an_unordered_value_is_given_none(string name)
    {
        var run = Run(ValueObjects + """
            public sealed partial record Sorted(int Value) : Value
            {
                public int CompareTo(Sorted? other) => 0;
            }
            public sealed record Bag;
            public sealed partial record Unordered(Bag Value) : Value;
            """);

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("IComparable", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DriverName", "string Value", "string? Value")]
    [InlineData("Litres", "decimal Value", "decimal? Value")]
    [InlineData("Pieces", "long Value", "long? Value")]
    [InlineData("InvoiceDay", "global::System.DateOnly Value", "global::System.DateOnly? Value")]
    [InlineData("FuelingMoment", "global::System.DateTime Value", "global::System.DateTime? Value")]
    [InlineData("KmRange", "int Start, int End", "int? Start, int? End")]
    [InlineData("ChipPeriod", "global::System.DateTime From, global::System.DateTime? To", "global::System.DateTime? From, global::System.DateTime? To")]
    [InlineData("Opening", "global::System.DateOnly? Date, decimal? Quantity", "global::System.DateOnly? Date, decimal? Quantity")]
    public void Each_base_gets_its_factories_over_its_primary_constructor(string name, string parameters, string optional)
    {
        var run = Run(ValueObjects);

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains($"public static {name} Create({parameters}, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains($"public static bool TryCreate({parameters}, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out {name}? result,", generated, StringComparison.Ordinal);
        Assert.Contains($"public static {name}? CreateOptional({optional}, string? field = null)", generated, StringComparison.Ordinal);
        Assert.Contains("= new " + name + "(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_value_object_of_one_value_is_registered_for_a_model_that_holds_it_as_it_is()
    {
        var run = Run(ValueObjects + "internal static partial class Hidden { private sealed partial record Secret(string Value) : Text(Value); }");

        var fields = run.GeneratedSource("RaskValueFields.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", fields, StringComparison.Ordinal);
        Assert.Contains(".Field<global::Shop.Pieces, long>(static v => v.Value, global::Shop.Pieces.TryCreate, static () => global::Shop.Pieces.__Field);", fields, StringComparison.Ordinal);
        Assert.Contains(".Field<global::Shop.DriverName, string>(", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("KmRange", fields, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", fields, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_value_object_of_one_value_gets_the_rule_a_form_asks()
    {
        var run = Run(ValueObjects);

        var name = run.GeneratedSource("DriverName.Value.g.cs");
        var range = run.GeneratedSource("KmRange.Value.g.cs");

        Assert.Contains("__Problems(string? value)", name, StringComparison.Ordinal);
        Assert.DoesNotContain("__Problems", range, StringComparison.Ordinal);
    }

    [Fact]
    public void A_moment_and_a_day_are_read_from_text_and_a_record_s_own_Parse_is_kept()
    {
        var run = Run("""
            public sealed partial record SentAt(System.DateTime Value) : Moment(Value);
            public sealed partial record DueOn(System.DateOnly Value) : Day(Value);
            public sealed partial record Seats(int Value) : Integer(Value, Min: 1);
            public sealed partial record OwnAt(System.DateTime Value) : Moment(Value)
            {
                public static OwnAt Parse(string text) => Create(System.DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
            }
            """);

        var sent = run.GeneratedSource("SentAt.Value.g.cs");
        var due = run.GeneratedSource("DueOn.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static SentAt Parse(string text, string? format = null, string? field = null) =>", sent, StringComparison.Ordinal);
        Assert.Contains("GeneratedValueRules.TryParseMoment(text, format, out var __read)", sent, StringComparison.Ordinal);
        Assert.Contains("GeneratedValueRules.ParseDay(text, format)", due, StringComparison.Ordinal);
        Assert.DoesNotContain("Parse(", run.GeneratedSource("Seats.Value.g.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Parse(", run.GeneratedSource("OwnAt.Value.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_of_one_member_declared_from_Value_gets_the_rule_a_form_asks_and_one_of_two_does_not()
    {
        var run = Run("""
            public sealed partial record Rank(int Value) : Value;
            public sealed partial record Pair(int A, int B) : Value;
            public sealed partial record Seats(int Value) : Integer(Value, Min: 1);
            """);

        var rank = run.GeneratedSource("Rank.Value.g.cs");
        var pair = run.GeneratedSource("Pair.Value.g.cs");
        var seats = run.GeneratedSource("Seats.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("__Problems(int? value)", rank, StringComparison.Ordinal);
        Assert.DoesNotContain("__Problems", pair, StringComparison.Ordinal);
        Assert.Contains("__Problems(int? value)", seats, StringComparison.Ordinal);
    }

    [Fact]
    public void A_one_value_value_object_gets_a_public_Check_in_the_shape_of_a_fields_rule()
    {
        var run = Run(ValueObjects + """
            public sealed class Field<T>
            {
                public Field<T> Validate(Rask.Core.Forms.Validate<T> rule) => this;
            }
            public static class Form
            {
                public static void Bind()
                {
                    new Field<string>().Validate(DriverName.Check);
                    new Field<decimal>().Validate(Litres.Check);
                    new Field<decimal?>().Validate(Litres.Check);
                }
            }
            """, "Rask.Core");

        var name = run.GeneratedSource("DriverName.Value.g.cs");
        var litres = run.GeneratedSource("Litres.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::System.Collections.Generic.IEnumerable<string> Check(string? value) => __Problems(value);", name, StringComparison.Ordinal);
        Assert.Contains("Check(decimal? value)", litres, StringComparison.Ordinal);
        Assert.Contains("Check(decimal value)", litres, StringComparison.Ordinal);
        Assert.DoesNotContain("Check(", run.GeneratedSource("KmRange.Value.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateOptional_refuses_only_the_members_that_may_not_be_empty()
    {
        var run = Run(ValueObjects);

        var range = run.GeneratedSource("KmRange.Value.g.cs");
        var period = run.GeneratedSource("ChipPeriod.Value.g.cs");

        Assert.Contains("Missing(\"Start\")", range, StringComparison.Ordinal);
        Assert.Contains("Missing(\"End\")", range, StringComparison.Ordinal);
        Assert.Contains("Missing(\"From\")", period, StringComparison.Ordinal);
        Assert.DoesNotContain("Missing(\"To\")", period, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DriverName", "__Facets => global::Rask.Core.Forms.StatedRules.Facets(typeof(DriverName), \"Value\", new(MaxLength: 1000));")]
    [InlineData("ChipCode", "__Facets => global::Rask.Core.Forms.StatedRules.Facets(typeof(ChipCode), \"Value\", new(MaxLength: 1000));")]
    [InlineData("Litres", "__Facets => global::Rask.Core.Forms.StatedRules.Facets(typeof(Litres), \"Value\", new(Scale: 2));")]
    public void The_column_facets_are_read_from_the_base_arguments_constants_included(string name, string facets)
    {
        var run = Run(ValueObjects);

        var generated = run.GeneratedSource(name + ".Value.g.cs");

        Assert.Contains(facets, generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_hands_the_column_the_facets_the_value_object_declares()
    {
        var run = GeneratorHarness.Run(
            Usings + ValueObjects + Holder,
            [new ValueObjectFactoryGenerator(), new ModelRegistryGenerator()],
            ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"]);

        var registry = run.GeneratedSource("ModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("(global::Shop.DriverName v) => v.Value, global::Shop.DriverName.__Facets)", registry, StringComparison.Ordinal);
        Assert.Contains("(global::Shop.Pieces v) => v.Value)", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_generated_member_is_documented_for_an_app_that_writes_its_documentation_file()
    {
        // An app with GenerateDocumentationFile and warnings as errors failed on CS1573 in the generated half: Create
        // documented its field and none of the value object's own parameters.
        var parse = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose);
        var run = Run(ValueObjects.Replace(
            "public sealed partial record", "/// <summary>A value.</summary>\npublic sealed partial record", StringComparison.Ordinal));
        var source = run.Compilation.SyntaxTrees.Single();

        var documented = run.RunResult.GeneratedTrees
            .Where(t => t.FilePath.EndsWith(".Value.g.cs", StringComparison.Ordinal))
            .Select(t => CSharpSyntaxTree.ParseText(
                t.GetText(TestContext.Current.CancellationToken), parse, t.FilePath, TestContext.Current.CancellationToken))
            .ToList();
        var compilation = run.Compilation
            .ReplaceSyntaxTree(source, source.WithRootAndOptions(source.GetRoot(TestContext.Current.CancellationToken), parse))
            .AddSyntaxTrees(documented);
        var findings = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Id.StartsWith("CS15", StringComparison.Ordinal) && d.Location.SourceTree is { } tree && documented.Contains(tree))
            .Select(d => d.Id + " " + d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(9, documented.Count);
        Assert.Empty(findings);
    }

    [Fact]
    public void Only_a_value_of_one_member_declared_from_Value_without_its_own_ToString_gets_one()
    {
        var run = Run("""
            public sealed partial record Chip(string Value) : Value;
            public sealed partial record Reading(decimal? Value) : Value;
            public sealed partial record Sign(int Value) : Value { public override string ToString() => "#"; }
            public sealed partial record Driver(string Value) : Text(Value);
            public sealed partial record Pair(int A, int B) : Value;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public override string ToString() => Value;", run.GeneratedSource("Chip.Value.g.cs"), StringComparison.Ordinal);
        Assert.Contains(
            "ToString() => Value?.ToString(null, global::System.Globalization.CultureInfo.InvariantCulture) ?? \"\";",
            run.GeneratedSource("Reading.Value.g.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("ToString()", run.GeneratedSource("Sign.Value.g.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ToString()", run.GeneratedSource("Driver.Value.g.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ToString()", run.GeneratedSource("Pair.Value.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_of_a_one_member_Value_record_prints_as_ISO_8601_like_Day_and_Moment()
    {
        var run = Run("""
            public sealed partial record Due(DateOnly Value) : Value;
            public sealed partial record Seen(DateTime? Value) : Value;
            """);

        var due = run.GeneratedSource("Due.Value.g.cs");
        var seen = run.GeneratedSource("Seen.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Value.ToString(\"yyyy-MM-dd\", global::System.Globalization.CultureInfo.InvariantCulture)", due, StringComparison.Ordinal);
        Assert.Contains("Value?.ToString(\"yyyy-MM-ddTHH:mm:ss\", global::System.Globalization.CultureInfo.InvariantCulture)", seen, StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_on_Rask_alone_gets_its_value_objects_Create_with_no_data_layer()
    {
        var run = GeneratorHarness.RunWithout(
            """
            using Rask.Values;
            namespace Parts;
            public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);
            public static class Use
            {
                public static Plate Make(string text) => Plate.Create(text, "Plate");
            }
            """,
            new ValueObjectFactoryGenerator(),
            ["Rask.Data", "Rask.Cqrs"],
            "Rask.Core", "Rask.Wire");

        var plate = run.GeneratedSource("Plate.Value.g.cs");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static Plate Create(", plate, StringComparison.Ordinal);
    }

    [Fact]
    public void No_module_initializer_is_written_since_the_bases_word_their_rules_from_the_catalog_themselves()
    {
        var run = Run(ValueObjects, "Rask.Core");

        var initializer = run.HasGeneratedSource("__RaskValueWords");

        Assert.False(initializer);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_value_object_nested_in_a_partial_type_is_generated_inside_it()
    {
        var run = Run("""
            public static partial class Fleet
            {
                public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);
            }
            """);

        var generated = run.GeneratedSource("Plate.Value.g.cs");

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("partial class Fleet", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_generated_model_asks_the_generated_rule_and_builds_through_Create()
    {
        var run = Run(ValueObjects + Holder);

        var model = run.GeneratedSource("DeliveryModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK132");
        Assert.Contains("global::Shop.DriverName.__Problems(held)", model, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(value) ? global::System.Array.Empty<string>() : global::Shop.DriverName.__Problems(value)", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Litres.__Problems(held)", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.DriverName.Create(", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.DriverName.CreateOptional(", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.InvoiceDay.CreateOptional(", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.FuelingMoment.__Problems(held)", model, StringComparison.Ordinal);
        Assert.Contains("global::Shop.FuelingMoment.Create(", model, StringComparison.Ordinal);
        Assert.DoesNotContain("new global::Shop.DriverName", model, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public partial record Plate(string Value) : Text(Value, MaxLength: 10);", "Text", "'sealed'")]
    [InlineData("public sealed record Plate(string Value) : Text(Value, MaxLength: 10);", "Text", "'partial'")]
    [InlineData("public record Plate(string Value) : Text(Value, MaxLength: 10);", "Text", "'sealed partial'")]
    [InlineData("public record Plate(int Start, int End) : Range(Start, End);", "Range", "'sealed partial'")]
    [InlineData("public sealed record Plate(int Count) : Value;", "Value", "'partial'")]
    public void A_record_that_is_not_sealed_partial_is_RASK129(string declaration, string valueBase, string lacks)
    {
        var run = Run(declaration);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK129");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Plate' is declared from Rask.Values." + valueBase + ", so it must be " + lacks, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.RunResult.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("Plate", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_object_nested_in_a_type_that_is_not_partial_is_RASK129()
    {
        var run = Run("""
            public static class Fleet
            {
                public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK129");

        Assert.Contains("'Fleet', which it is nested in", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sealed_partial_record_and_an_ordinary_record_are_not_RASK129()
    {
        var run = Run(ValueObjects + "public record Other(string Value);");

        var diagnostics = run.Diagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Id == "RASK129");
    }

    // ---- RASK127 ----

    private const string Plate = """
        using Rask.Data;
        using Rask.Values;
        using Range = Rask.Values.Range;
        namespace Shop;
        public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10)
        {
            public static Plate Blank() => new("x");
            public Plate Same() => this with { };
        }
        public sealed partial record Km(int Start, int End) : Range(Start, End);
        public static class Elsewhere
        {
            ELSEWHERE
        }
        """;

    [Theory]
    [InlineData("public static Plate Make() => new Plate(\"a\");", "Plate", "Text")]
    [InlineData("public static Km Make() => new(1, 2);", "Km", "Range")]
    public async Task A_value_object_made_with_new_outside_its_type_is_RASK127(string elsewhere, string name, string valueBase)
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(), Plate.Replace("ELSEWHERE", elsewhere, StringComparison.Ordinal));

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK127", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{name}' is declared from Rask.Values.{valueBase}, so it is made only by '{name}.Create(…)'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_with_outside_its_type_is_RASK127()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(), Plate.Replace("ELSEWHERE", "public static Km Copy(Km k) => k with { End = 5 };", StringComparison.Ordinal));

        var diagnostic = Assert.Single(diagnostics);

        Assert.Contains("instead of 'with'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_and_with_inside_its_own_type_and_on_other_records_are_fine()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ValueObjectCreationAnalyzer(),
            Plate.Replace("ELSEWHERE", "public record Other(string Value); public static Other Make() => new Other(\"a\") with { };", StringComparison.Ordinal));

        var none = diagnostics;

        Assert.Empty(none);
    }

    // ---- RASK128 ----

    private static Task<IReadOnlyList<Diagnostic>> Configured(string configure) =>
        ModelStateAnalyzerHarness.RunAsync(new ValueObjectColumnFacetAnalyzer(), $$"""
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            using Rask.Values;
            namespace Shop;
            public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);
            public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2, Precision: 9);
            public sealed partial record Note(string Value) : Text(Value, 0, 2);
            public sealed partial record Ordered(string Value) : Text(Value, 1000);
            public sealed class Delivery : Aggregate<Guid>
            {
                private Delivery() { }
                public DriverName Driver { get; private set; } = null!;
                public Litres Fuel { get; private set; } = null!;
                public Note Remark { get; private set; } = null!;
                public Ordered Line { get; private set; } = null!;
                public static void Configure(EntityTypeBuilder<Delivery> builder)
                {
                    {{configure}}
                }
            }
            """);

    [Theory]
    [InlineData("builder.Property(d => d.Driver).HasMaxLength(200);", "declares MaxLength: 1000, but 'HasMaxLength' gives its column a length of 200")]
    [InlineData("builder.Property(d => d.Line).HasMaxLength(200);", "declares MaxLength: 1000, but 'HasMaxLength' gives its column a length of 200")]
    [InlineData("builder.Property(d => d.Driver).HasColumnType(\"nvarchar(max)\");", "gives its column no length")]
    [InlineData("builder.Property(d => d.Fuel).HasPrecision(18, 4);", "declares Precision: 9, Scale: 2, but 'HasPrecision' gives its column a precision of 18 and a scale of 4")]
    [InlineData("builder.Property(d => d.Fuel).HasColumnType(\"decimal(9,3)\");", "a precision of 9 and a scale of 3")]
    public async Task A_configured_facet_other_than_the_value_objects_is_RASK128(string configure, string said)
    {
        var diagnostics = await Configured(configure);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK128", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains(said, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builder.Property(d => d.Driver).HasMaxLength(1000);")]
    [InlineData("builder.Property(d => d.Fuel).HasPrecision(9, 2);")]
    [InlineData("builder.Property(d => d.Fuel).HasColumnType(\"decimal(9, 2)\");")]
    [InlineData("builder.Property(d => d.Remark).HasMaxLength(50);")]
    [InlineData("var length = 200; builder.Property(d => d.Driver).HasMaxLength(length);")]
    public async Task A_configured_facet_that_agrees_or_is_not_literal_is_fine(string configure)
    {
        var diagnostics = await Configured(configure);

        var none = diagnostics;

        Assert.Empty(none);
    }

    private static Task<IReadOnlyList<Diagnostic>> Attributed(string attribute, string property) =>
        ModelStateAnalyzerHarness.RunAsync(new ValueObjectColumnFacetAnalyzer(), $$"""
            using System;
            using System.ComponentModel.DataAnnotations;
            using Microsoft.EntityFrameworkCore;
            using Rask.Data;
            using Rask.Values;
            namespace Shop;
            public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);
            public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2, Precision: 9);
            public sealed class Delivery : Aggregate<Guid>
            {
                private Delivery() { }
                {{attribute}}
                {{property}}
            }
            """);

    [Theory]
    [InlineData("[MaxLength(200)]", "public DriverName Driver { get; private set; } = null!;", "declares MaxLength: 1000, but [MaxLength] gives its column a length of 200")]
    [InlineData("[StringLength(50)]", "public DriverName? Driver { get; private set; }", "declares MaxLength: 1000, but [StringLength] gives its column a length of 50")]
    [InlineData("[MaxLength]", "public DriverName Driver { get; private set; } = null!;", "[MaxLength] gives its column no length")]
    [InlineData("[Precision(18, 4)]", "public Litres Fuel { get; private set; } = null!;", "declares Precision: 9, Scale: 2, but [Precision] gives its column a precision of 18 and a scale of 4")]
    public async Task An_attribute_giving_the_column_a_facet_other_than_the_value_objects_is_RASK128(string attribute, string property, string said)
    {
        var diagnostics = await Attributed(attribute, property);

        var diagnostic = Assert.Single(diagnostics);

        Assert.Equal("RASK128", diagnostic.Id);
        Assert.Contains(said, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.StartsWith(attribute.Trim('[', ']'), diagnostic.Location.SourceTree!.ToString()[diagnostic.Location.SourceSpan.Start..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[MaxLength(1000)]", "public DriverName Driver { get; private set; } = null!;")]
    [InlineData("[StringLength(1000)]", "public DriverName Driver { get; private set; } = null!;")]
    [InlineData("[Precision(9, 2)]", "public Litres Fuel { get; private set; } = null!;")]
    [InlineData("[Required]", "public DriverName? Driver { get; private set; }")]
    [InlineData("[MaxLength(10)]", "public string Name { get; private set; } = \"\";")]
    public async Task An_attribute_that_agrees_with_the_value_object_or_gives_no_facet_is_fine(string attribute, string property)
    {
        var diagnostics = await Attributed(attribute, property);

        var none = diagnostics;

        Assert.Empty(none);
    }

    // ---- RASK130 ----

    private const string Entity = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public enum Kind { A, B }
        public readonly record struct CustomerId(Guid Value);
        public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100);
        public sealed class Delivery : Aggregate<Guid, Timestamps, Versioned>
        {
            private Delivery() { }
            public string Note { get; private set; } = "";
            public decimal? Litres { get; private set; }
            public DateOnly Day { get; private set; }
            public Guid Reference { get; private set; }
            public bool Done { get; private set; }
            public Kind Kind { get; private set; }
            public CustomerId Customer { get; private set; }
            public DriverName Driver { get; private set; } = null!;
            public List<string> Tags { get; private set; } = [];
            public string Display => Note + "!";
        }
        """;

    [Fact]
    public async Task A_bare_primitive_an_aggregate_stores_is_a_RASK130_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new PrimitiveStatePropertyAnalyzer(), Entity);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(["Note", "Litres", "Day", "Reference"], flagged);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        Assert.Contains("'Delivery.Litres' is a bare 'decimal?' — declare a value object for it", diagnostics[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_bare_primitive()
    {
        var strict = StrictConfig.Severity("RASK130");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new PrimitiveStatePropertyAnalyzer(), Entity, options => options.WithSpecificDiagnosticOptions([new("RASK130", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    [Fact]
    public async Task A_class_that_is_not_an_entity_is_not_RASK130()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new PrimitiveStatePropertyAnalyzer(), "namespace Shop; public sealed class Form { public string Name { get; set; } = \"\"; }");

        var none = diagnostics;

        Assert.Empty(none);
    }
}
