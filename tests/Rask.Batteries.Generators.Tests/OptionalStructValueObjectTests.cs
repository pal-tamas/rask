using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A one-value STRUCT value object an aggregate holds as <c>T?</c> is a value object, and optional — in every
/// generator the convention reaches, exactly as a nullable reference-type value object is.
/// </summary>
public class OptionalStructValueObjectTests
{
    private const string Shop = """
        using System;
        using System.Collections.Generic;
        using Rask.Cqrs;
        using Rask.Data;
        namespace Shop;
        public readonly record struct Percent(decimal Value)
        {
            public static IEnumerable<string> Validate(decimal value) => value < 0 ? ["Negative."] : [];
        }
        public readonly record struct Money(decimal Amount, string Currency);

        """;

    private const string Norm = """
        public sealed class Norm : Aggregate<Guid>
        {
            private Norm() { }
            public Percent? Discount { get; private set; }
            public Percent Rate { get; private set; }
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            Shop + source,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator(), new CqrsCodecGenerator()],
            "Rask.Data", "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void The_form_model_carries_the_held_value_as_nullable_and_writes_it_back_as_the_struct_or_nothing()
    {
        var run = Run(Norm);

        var model = run.GeneratedSource("Shop.NormModel").ReplaceLineEndings("\n");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("    public decimal? Discount { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains("__Setter0_Discount(entity, new global::Shop.Percent(__v0));", model, StringComparison.Ordinal);
        Assert.Contains("__Setter0_Discount(entity, null);", model, StringComparison.Ordinal);
        Assert.Contains("model.Discount = entity.Discount?.Value;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_rule_is_asked_when_the_field_is_filled_and_an_empty_field_is_neither_asked_nor_required()
    {
        var run = Run(Norm);

        var model = run.GeneratedSource("Shop.NormModel").ReplaceLineEndings("\n");

        Assert.Contains(
            "__Rule_Root_Discount(decimal? value) =>\n        value is { } held ? global::Shop.Percent.Validate(held) : global::System.Array.Empty<string>();",
            model,
            StringComparison.Ordinal);
        Assert.Contains(
            "__Rule_Root_Rate(decimal? value) =>\n        value is { } held ? global::Shop.Percent.Validate(held) : global::Rask.Data.GeneratedModelRules.Required(__RequiredText());",
            model,
            StringComparison.Ordinal);
        Assert.Contains("Told(failures, path, \"Discount\", __Rule_Root_Discount(model.Discount));", model, StringComparison.Ordinal);
        Assert.Contains("Root_IsRequired(string property) =>\n        property is \"Rate\";", model, StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_stored_as_a_converted_scalar_column_and_read_as_a_nullable_primitive()
    {
        var run = Run(Norm);

        var registry = run.GeneratedSource("__RaskModelRegistry");
        var face = run.GeneratedSource("Shop_NormRead");
        var mapping = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Contains(
            "global::Rask.Data.ValueObjectColumn.Map(__Shop_Norm.Property(x => x.Discount), (global::Shop.Percent v) => v.Value);",
            registry,
            StringComparison.Ordinal);
        Assert.Contains("public decimal? Discount { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public decimal Rate { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("ReadColumnMapping(\"Discount\", \"Discount.Value\", \"Discount\")", mapping, StringComparison.Ordinal);
    }

    [Fact]
    public void It_crosses_the_wire_as_the_nullable_value_the_model_declares()
    {
        var run = Run(Norm + """

            public sealed record SaveNorm(NormModel Norm) : ICommand;
            """);

        var codec = run.GeneratedSource("__RaskCqrsCodecs");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Discount = v_Discount,", codec, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Shop.Percent", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void Inside_another_value_object_it_is_a_field_of_the_nested_model_and_a_converted_scalar_column()
    {
        var run = Run("""
            public sealed record Size(Percent? Tolerance, int Units);
            public sealed class Norm : Aggregate<Guid>
            {
                private Norm() { }
                public Size Size { get; private set; } = new(null, 0);
            }
            """);

        var model = run.GeneratedSource("Shop.NormModel").ReplaceLineEndings("\n");
        var registry = run.GeneratedSource("__RaskModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public decimal? Tolerance { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains(
            "__Rule_SizeModel_Tolerance(decimal? value) =>\n        value is { } held ? global::Shop.Percent.Validate(held) : global::System.Array.Empty<string>();",
            model,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Data.ValueObjectColumn.Map(b0.Property(x => x.Tolerance), (global::Shop.Percent v) => v.Value).HasColumnName(\"Size_Tolerance\");",
            registry,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_struct_of_several_values_is_left_as_the_plain_value_it_was()
    {
        var run = Run("""
            public sealed class Norm : Aggregate<Guid>
            {
                private Norm() { }
                public Money? Price { get; private set; }
            }
            """);

        var model = run.GeneratedSource("Shop.NormModel");
        var registry = run.GeneratedSource("__RaskModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::Shop.Money? Price { get; set; }", model, StringComparison.Ordinal);
        Assert.DoesNotContain("MoneyModel", model, StringComparison.Ordinal);
        Assert.DoesNotContain("x.Price", registry, StringComparison.Ordinal);
    }
}
