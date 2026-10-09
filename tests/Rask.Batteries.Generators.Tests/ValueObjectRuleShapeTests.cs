using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// RASK102: a <c>Validate</c> on a one-value value object an aggregate holds, which looks meant as its rule
/// and does not have the shape of one — so no form would run it, and nothing else would say so.
/// </summary>
public class ValueObjectRuleShapeTests
{
    private static List<Diagnostic> Rask102(string valueObjects, string holder = Holder) =>
        [.. GeneratorHarness.Run(
                Usings + valueObjects + holder,
                new ModelInputGenerator(),
                "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore")
            .Diagnostics.Where(d => d.Id == "RASK102")];

    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.Threading.Tasks;
        using Rask.Data;
        namespace Shop;

        """;

    private const string Holder = """

        public sealed class Product : Aggregate<Guid>
        {
            private Product() { }
            public Sku Sku { get; private set; } = new("");
        }
        """;

    [Theory]
    [InlineData("public static IEnumerable<string> Validate(int value) => [];", "its parameter is 'int', not the 'string' it holds")]
    [InlineData("public static IEnumerable<string> Validate(Sku value) => [];", "its parameter is 'Sku', not the 'string' it holds")]
    [InlineData("internal static IEnumerable<string> Validate(string value) => [];", "it is not public")]
    [InlineData("static IEnumerable<string> Validate(string value) => [];", "it is not public")]
    [InlineData("public IEnumerable<string> Validate(string value) => [];", "it is not static")]
    [InlineData("public IEnumerable<string> Validate() => [];", "it is not static")]
    [InlineData("public static IEnumerable<string> Validate<T>(T value) => [];", "it is generic")]
    [InlineData("public static IEnumerable<string> Validate() => [];", "it takes 0 parameters, not the one value")]
    [InlineData("public static IEnumerable<string> Validate(string value, bool strict) => [];", "it takes 2 parameters, not the one value")]
    [InlineData("public static string Validate(string value) => \"\";", "it returns 'string', not the messages")]
    [InlineData("public static bool Validate(string value) => true;", "it returns 'bool', not the messages")]
    [InlineData("public static List<string> Validate(string value) => [];", "it returns 'List<string>', not the messages")]
    [InlineData("public static Task<string> Validate(string value) => Task.FromResult(\"\");", "it returns 'Task<string>', not the messages")]
    public void A_Validate_that_is_not_the_shape_of_a_rule_is_reported_with_what_is_wrong_and_what_would_qualify(
        string method, string reason)
    {
        var source = $$"""
            public sealed record Sku(string Value)
            {
                {{method}}
            }
            """;

        var diagnostic = Assert.Single(Rask102(source));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("on value object 'Sku' is not run by any form because " + reason, diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.EndsWith(
            "a rule is 'public static IEnumerable<string> Validate(string value)', or the same returning "
            + "ValueTask<IEnumerable<string>> or Task<IEnumerable<string>>",
            diagnostic.GetMessage(),
            StringComparison.Ordinal);

        // On the method itself — the ninth line of the source — not on the value object or the aggregate.
        Assert.Equal(8, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void The_message_names_the_method_as_it_was_written()
    {
        var diagnostic = Assert.Single(Rask102("""
            public sealed record Sku(string Value)
            {
                public static IEnumerable<string> Validate(int value) => [];
            }
            """));

        Assert.StartsWith("'Sku.Validate(int)' on value object 'Sku'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static IEnumerable<string> Validate(string value) => [];")]
    [InlineData("public static ValueTask<IEnumerable<string>> Validate(string value) => new(Array.Empty<string>());")]
    [InlineData("public static Task<IEnumerable<string>> Validate(string value) => Task.FromResult<IEnumerable<string>>([]);")]
    [InlineData("public static IEnumerable<string> Validate(string value) => []; public static bool Validate(int other) => true;")]
    [InlineData("public static IEnumerable<string> Check(int value) => [];")]
    [InlineData("")]
    public void A_rule_of_the_right_shape_and_overloads_beside_one_and_other_names_are_not_reported(string members)
    {
        var source = $$"""
            public sealed record Sku(string Value)
            {
                {{members}}
            }
            """;

        var diagnostics = Rask102(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_Validate_that_fulfils_somebody_elses_contract_is_not_taken_for_the_rule()
    {
        var diagnostics = Rask102("""
            public abstract record Checked
            {
                public abstract bool Validate();
            }
            public sealed record Sku(string Value) : Checked, IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];
                public override bool Validate() => true;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_value_object_of_several_values_is_not_reported_whatever_its_Validate_looks_like()
    {
        var diagnostics = Rask102(
            """
            public sealed record Money(decimal Amount, string Currency)
            {
                public static IEnumerable<string> Validate(Money value) => [];
                public bool Validate() => true;
            }
            """,
            """

            public sealed class Product : Aggregate<Guid>
            {
                private Product() { }
                public Money Price { get; private set; } = new(0, "EUR");
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_type_no_aggregate_holds_and_the_aggregate_itself_are_not_reported()
    {
        var diagnostics = Rask102(
            """
            public sealed record Loose(string Value)
            {
                public bool Validate() => true;
                public static string Validate(int value) => "";
            }
            public sealed class Basket
            {
                public bool Validate() => true;
            }
            """,
            """

            public sealed class Product : Aggregate<Guid>
            {
                private Product() { }
                public string Title { get; private set; } = "";
                public bool Validate() => Title.Length > 0;
                public static string Validate(int value) => "";
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void A_value_object_two_aggregates_hold_and_one_held_inside_another_are_each_reported_once()
    {
        var diagnostics = Rask102(
            """
            public sealed record Sku(string Value)
            {
                public static bool Validate(string value) => true;
            }
            public sealed record Code(string Value)
            {
                public static bool Validate(string value) => true;
            }
            public sealed record Size(Code Code, int Units);
            """,
            """

            public sealed class Product : Aggregate<Guid>
            {
                private Product() { }
                public Sku Sku { get; private set; } = new("");
                public Size Size { get; private set; } = new(new(""), 0);
            }
            public sealed class Bundle : Aggregate<Guid>
            {
                private Bundle() { }
                public Sku Sku { get; private set; } = new("");
            }
            """);

        Assert.Equal(
            ["'Code.Validate(string)'", "'Sku.Validate(string)'"],
            diagnostics.Select(d => d.GetMessage()[..d.GetMessage().IndexOf(" on ", StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_row_inside_an_aggregate_reports_its_value_objects_too()
    {
        var diagnostics = Rask102(
            """
            public sealed record Sku(string Value)
            {
                public static bool Validate(string value) => true;
            }
            """,
            """

            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                private Order() { }
                public IReadOnlyList<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>
            {
                private OrderLine() { }
                public Sku Sku { get; private set; } = new("");
            }
            """);

        Assert.Single(diagnostics);
    }
}
