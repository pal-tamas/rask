using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK132: a static <c>Validate</c> on a one-value value object an aggregate holds that is not declared from a
/// Rask base record — the earlier way to state a rule, which no form and no generated write asks any more.
/// </summary>
public class ValueObjectStaleRuleTests
{
    private static List<Diagnostic> Rask132(string valueObjects, string holder = Holder) =>
        [.. GeneratorHarness.Run(
                Usings + valueObjects + holder,
                [new ValueObjectFactoryGenerator(), new ModelInputGenerator()],
                ["Rask.Data", "Rask.Cqrs", "Rask.Wire", "Microsoft.EntityFrameworkCore"])
            .Diagnostics.Where(d => d.Id == "RASK132")];

    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.Threading.Tasks;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;

        """;

    private const string Holder = """

        public sealed class Product : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
        {
            private Product() { }
            public Sku Sku { get; private set; } = new(default!);
        }
        """;

    [Theory]
    [InlineData("string", "Text")]
    [InlineData("decimal", "Number")]
    [InlineData("long", "Integer")]
    [InlineData("int", "Integer")]
    [InlineData("DateOnly", "Day")]
    public void A_Validate_beside_the_value_names_the_base_record_that_takes_the_value(string held, string declaredFrom)
    {
        var source = $$"""
            public readonly record struct Sku({{held}} Value)
            {
                public static IEnumerable<string> Validate({{held}} value) => [];
            }
            """;

        var diagnostic = Assert.Single(Rask132(source));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            $"'Sku.Validate({held})' on value object 'Sku' is never asked: no form and no generated write runs a value object's "
            + "Validate, only the rules of one declared from a Rask base record — declare it "
            + $"'public sealed partial record Sku({held} Value) : {declaredFrom}(Value, …);' with its rules as the base's "
            + "arguments, or ': Value' with them in 'protected override IEnumerable<Problem> Rules()', and make it with 'Sku.Create(…)'",
            diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("Guid")]
    public void A_value_no_base_record_takes_is_told_to_derive_from_Value_with_its_own_rules(string held)
    {
        var source = $$"""
            public readonly record struct Sku({{held}} Value)
            {
                public static IEnumerable<string> Validate({{held}} value) => [];
            }
            """;

        var message = Assert.Single(Rask132(source)).GetMessage();

        Assert.Contains(
            $"declare it 'public sealed partial record Sku({held} Value) : Value;' with its rules in "
            + "'protected override IEnumerable<Problem> Rules()', and make it with 'Sku.Create(…)'",
            message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static ValueTask<IEnumerable<string>> Validate(string value) => new(Array.Empty<string>());")]
    [InlineData("public static Task<IEnumerable<string>> Validate(string value) => Task.FromResult<IEnumerable<string>>([]);")]
    [InlineData("public static string Validate(string value) => \"\";")]
    [InlineData("internal static IEnumerable<string> Validate(string? value, bool strict) => [];")]
    public void A_Validate_of_any_shape_is_reported_at_the_method(string member)
    {
        var source = $$"""
            public sealed record Sku(string Value)
            {
                {{member}}
            }
            """;

        var diagnostic = Assert.Single(Rask132(source));

        Assert.Equal("Validate", source.Substring(diagnostic.Location.SourceSpan.Start - Usings.Length, "Validate".Length));
    }

    [Theory]
    [InlineData("public static IEnumerable<string> Check(string value) => [];")]
    [InlineData("public bool Validate() => Value.Length > 0;")]
    [InlineData("")]
    public void Another_name_and_an_instance_Validate_are_not_reported(string members)
    {
        var source = $$"""
            public sealed record Sku(string Value)
            {
                {{members}}
            }
            """;

        var diagnostics = Rask132(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_value_object_declared_from_a_base_record_is_not_reported_whatever_else_it_declares()
    {
        const string source = """
            public sealed partial record Sku(string Value) : Text(Value, MaxLength: 20)
            {
                public static IEnumerable<string> Validate(string value) => [];
            }
            """;

        var diagnostics = Rask132(source, """

            public sealed class Product : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
            {
                private Product() { }
                public Sku Sku { get; private set; } = Sku.Create("A-1");
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_Validate_that_fulfils_somebody_elses_contract_is_not_reported()
    {
        var diagnostics = Rask132("""
            public sealed record Sku(string Value) : IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_value_object_of_several_values_and_a_type_no_aggregate_holds_are_not_reported()
    {
        var diagnostics = Rask132(
            """
            public sealed record Money(decimal Amount, string Currency)
            {
                public static IEnumerable<string> Validate(Money value) => [];
            }
            public sealed record Loose(string Value)
            {
                public static IEnumerable<string> Validate(string value) => [];
            }
            """,
            """

            public sealed class Product : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
            {
                private Product() { }
                public Money Price { get; private set; } = new(0, "EUR");
                public static string Validate(int value) => "";
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_value_object_two_aggregates_hold_one_held_inside_another_and_one_on_a_row_are_each_reported_once()
    {
        var diagnostics = Rask132(
            """
            public sealed record Sku(string Value)
            {
                public static IEnumerable<string> Validate(string value) => [];
            }
            public sealed record Code(string Value)
            {
                public static IEnumerable<string> Validate(string value) => [];
            }
            public sealed record Tag(string Value)
            {
                public static IEnumerable<string> Validate(string value) => [];
            }
            public sealed record Size(Code Code, int Units);
            """,
            """

            public sealed class Product : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
            {
                private readonly List<ProductLine> _lines = [];
                private Product() { }
                public Sku Sku { get; private set; } = new("");
                public Size Size { get; private set; } = new(new(""), 0);
                public IReadOnlyList<ProductLine> Lines => _lines;
            }
            public sealed class Bundle : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
            {
                private Bundle() { }
                public Sku Sku { get; private set; } = new("");
            }
            public sealed class ProductLine : Entity<Guid>
            {
                private ProductLine() { }
                public Tag Tag { get; private set; } = new("");
            }
            """);

        Assert.Equal(
            ["'Code.Validate(string)'", "'Sku.Validate(string)'", "'Tag.Validate(string)'"],
            diagnostics.Select(d => d.GetMessage()[..d.GetMessage().IndexOf(" on ", StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
    }
}
