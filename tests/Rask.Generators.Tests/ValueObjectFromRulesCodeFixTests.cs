using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>RASK130's quick-fix: a bare property becomes a value object declared from the rules written for it.</summary>
public class ValueObjectFromRulesCodeFixTests
{
    private static string Supplier(string member, string rules = "") => $$"""
        using System;
        using System.ComponentModel.DataAnnotations;
        using FluentValidation;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed partial class Supplier : Aggregate<Guid>
        {
            private Supplier() { }
            {{member}}

            static void Rules(InlineValidator<Supplier> rules)
            {
                {{rules}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new PrimitiveStatePropertyAnalyzer(), new ValueObjectFromRulesCodeFixProvider(), "RASK130", source,
            "Rask.Data", "Rask.Cqrs", "FluentValidation");

    private static Task<bool> Offered(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new PrimitiveStatePropertyAnalyzer(), new ValueObjectFromRulesCodeFixProvider(), "RASK130", source,
            "Rask.Data", "Rask.Cqrs", "FluentValidation");

    [Theory]
    [InlineData("[StringLength(200)] public string Name { get; private set; } = \"\";", "Text(Value, MaxLength: 200, Required: false)")]
    [InlineData("[Required, StringLength(200, MinimumLength = 2)] public string Name { get; private set; } = \"\";", "Text(Value, MaxLength: 200, MinLength: 2)")]
    [InlineData("[MaxLength(50)] [MinLength(1)] public string Name { get; private set; } = \"\";", "Text(Value, MaxLength: 50, MinLength: 1)")]
    public async Task A_length_attribute_becomes_the_Text_base_argument_that_says_it(string member, string declared)
    {
        var source = Supplier(member);

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial record SupplierName(string Value) : " + declared + ";", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Length(", fixed_, StringComparison.Ordinal);
        Assert.Contains("public SupplierName Name { get; private set; } = null!;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_FluentValidation_rule_becomes_the_Text_base_arguments_and_leaves_the_Rules()
    {
        var source = Supplier("public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);");

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleFor", fixed_, StringComparison.Ordinal);
        Assert.Contains("public SupplierName Name { get; private set; } = null!;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_no_base_argument_says_stays_on_the_rule_with_its_message()
    {
        var source = Supplier(
            "public string Code { get; private set; } = \"\";",
            "rules.RuleFor(s => s.Code).Length(3).WithMessage(\"three\").Matches(\"^[A-Z]+$\").WithMessage(\"capitals\");");

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial record SupplierCode(string Value) : Text(Value, MaxLength: 3, MinLength: 3);", fixed_, StringComparison.Ordinal);
        Assert.Contains("rules.RuleFor(s => s.Code).Matches(\"^[A-Z]+$\").WithMessage(\"capitals\");", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[Range(0, 1000)] public decimal Litres { get; private set; }", "", "SupplierLitres(decimal Value) : Number(Value, Min: 0, Max: 1000)")]
    [InlineData("public decimal Litres { get; private set; }", "rules.RuleFor(s => s.Litres).InclusiveBetween(0.5m, 99.5m).PrecisionScale(9, 2, false);", "SupplierLitres(decimal Value) : Number(Value, Min: 0.5m, Max: 99.5m, Scale: 2, Precision: 9)")]
    [InlineData("public int Rank { get; private set; }", "rules.RuleFor(s => s.Rank).GreaterThanOrEqualTo(1).LessThanOrEqualTo(10);", "SupplierRank(int Value) : Integer(Value, Min: 1, Max: 10)")]
    [InlineData("public DateOnly Since { get; private set; }", "", "SupplierSince(DateOnly Value) : Day(Value)")]
    public async Task A_number_or_a_day_becomes_the_base_record_that_holds_it(string member, string rules, string declared)
    {
        var source = Supplier(member, rules);

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial record " + declared + ";", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("RuleFor", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("[Range", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_optional_property_becomes_an_optional_value_object_and_keeps_saying_it_must_be_given()
    {
        var source = Supplier("public string? Email { get; private set; }", "rules.RuleFor(s => s.Email).NotEmpty().MaximumLength(254);");

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial record SupplierEmail(string Value) : Text(Value, MaxLength: 254);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public SupplierEmail? Email { get; private set; }", fixed_, StringComparison.Ordinal);
        Assert.Contains("rules.RuleFor(s => s.Email).NotEmpty();", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expression_bodied_Rules_left_with_nothing_becomes_an_empty_one()
    {
        const string source = """
            using System;
            using FluentValidation;
            using Rask.Data;
            namespace Shop;
            public sealed partial class Supplier : Aggregate<Guid>
            {
                private Supplier() { }
                public string Name { get; private set; } = "";
                static void Rules(InlineValidator<Supplier> rules) => rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);
            }
            """;

        var fixed_ = await Fix(source);

        Assert.Matches(@"static void Rules\(InlineValidator<Supplier> rules\)\s*\{\s*\}", fixed_);
        Assert.Contains("using Rask.Values;", fixed_, StringComparison.Ordinal);
        Assert.Contains("public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[StringLength(200, ErrorMessage = \"Too long\")] public string Name { get; private set; } = \"\";", "")]
    [InlineData("[EmailAddress] public string Name { get; private set; } = \"\";", "")]
    [InlineData("[MaxLength(100)] public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).MaximumLength(200);")]
    [InlineData("public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).MaximumLength(200).When(s => s.Name.Length > 0);")]
    [InlineData("public Guid Reference { get; private set; }", "")]
    [InlineData("public string Name { get; private set; } = \"\"; public sealed partial record SupplierName(string Value) : Text(Value);", "")]
    public async Task No_fix_is_offered_where_the_value_object_would_not_say_what_was_written(string member, string rules)
    {
        var source = Supplier(member, rules);

        var offered = await Offered(source);

        Assert.False(offered);
    }

    [Fact]
    public async Task No_fix_is_offered_while_a_validator_class_rules_the_same_property()
    {
        var source = Supplier("[StringLength(200)] public string Name { get; private set; } = \"\";") + """

            public sealed class SupplierValidator : FluentValidation.AbstractValidator<Shop.Supplier>
            {
                public SupplierValidator() => RuleFor(s => s.Name).Must(n => n.Length > 0);
            }
            """;

        var offered = await Offered(source);

        Assert.False(offered);
    }
}
