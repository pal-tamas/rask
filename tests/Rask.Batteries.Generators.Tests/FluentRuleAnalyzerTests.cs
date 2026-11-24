using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     RASK181 (a FluentValidation rule on a value-object member) and RASK182 (a FluentValidation length or precision the
///     column does not take), in the in-type <c>Rules(InlineValidator&lt;T&gt;)</c> and in an <c>AbstractValidator&lt;T&gt;</c>.
/// </summary>
public class FluentRuleAnalyzerTests
{
    private static string Supplier(string members, string rules) => $$"""
        using System;
        using System.ComponentModel.DataAnnotations;
        using FluentValidation;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100);
        public sealed partial class Supplier : Aggregate<Guid>
        {
            private Supplier() { }
            {{members}}
            static void Rules(InlineValidator<Supplier> rules)
            {
                {{rules}}
            }
        }
        """;

    // ---- RASK181 ----

    [Theory]
    [InlineData("rules.RuleFor(s => s.Name).Must(n => n.Value.Length <= 200);")]
    [InlineData("rules.RuleFor(s => s.Name).NotEmpty().Must(n => n.Value.Length > 1);")]
    public async Task A_rule_in_the_types_Rules_on_a_value_object_member_is_RASK181(string rule)
    {
        var source = Supplier("public SupplierName Name { get; private set; } = null!;", rule);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleOnValueObjectAnalyzer(), source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK181", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("'Supplier.Name' is a 'SupplierName', whose own rules guard it — move the rule into 'SupplierName'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rule_in_an_AbstractValidator_constructor_on_a_value_object_member_is_RASK181()
    {
        var source = Supplier("public SupplierName Name { get; private set; } = null!;", "") + """

            public sealed class SupplierValidator : AbstractValidator<Supplier>
            {
                public SupplierValidator() => RuleFor(s => s.Name).Must(n => n.Value != "x");
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleOnValueObjectAnalyzer(), source);

        Assert.Equal("RuleFor(s => s.Name)", Assert.Single(diagnostics).Flagged());
    }

    [Theory]
    [InlineData("public SupplierName? Name { get; private set; }", "rules.RuleFor(s => s.Name).NotNull();")]
    [InlineData("public SupplierName? Name { get; private set; }", "rules.RuleFor(s => s.Name).NotEmpty().WithMessage(\"Name it\");")]
    [InlineData("public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);")]
    public async Task Saying_a_value_object_is_given_or_ruling_a_primitive_is_not_RASK181(string member, string rule)
    {
        var source = Supplier(member, rule);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleOnValueObjectAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_rule_on_a_value_object_member_of_a_form_model_is_not_RASK181()
    {
        const string source = """
            using FluentValidation;
            using Rask.Values;
            namespace Shop;
            public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100);
            public sealed class SupplierForm
            {
                public SupplierName Name { get; set; } = null!;
                static void Rules(InlineValidator<SupplierForm> rules) => rules.RuleFor(s => s.Name).Must(n => n.Value != "x");
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleOnValueObjectAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    // ---- RASK182 ----

    [Theory]
    [InlineData("[MaxLength(100)] public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);", "MaximumLength(200)", "[MaxLength(100)]")]
    [InlineData("[StringLength(100)] public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).Length(1, 150);", "Length(1, 150)", "[StringLength(100)]")]
    [InlineData("[Precision(12, 4)] public decimal Litres { get; private set; }", "rules.RuleFor(s => s.Litres).PrecisionScale(10, 2, false);", "PrecisionScale(10, 2, false)", "[Precision(12, 4)]")]
    public async Task A_length_or_precision_the_attribute_does_not_give_is_RASK182(string member, string rule, string flagged, string source)
    {
        var code = Supplier(member, rule);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleFacetAnalyzer(), code);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK182", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(flagged, d.Flagged());
        Assert.Contains("but " + source + " gives it", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("the column takes", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_value_objects_own_rule_that_disagrees_with_its_base_is_RASK182()
    {
        const string source = """
            using FluentValidation;
            using Rask.Values;
            namespace Shop;
            public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100)
            {
                static void Rules(InlineValidator<SupplierName> rules) => rules.RuleFor(n => n.Value).MaximumLength(200);
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleFacetAnalyzer(), source);

        var d = Assert.Single(diagnostics);
        Assert.Equal("MaximumLength(200)", d.Flagged());
        Assert.Contains("The rule gives 'SupplierName.Value' a length of at most 200, but its base Text(MaxLength: 100) gives it a length of at most 100", d.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[MaxLength(200)] public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).MaximumLength(200);")]
    [InlineData("public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).MaximumLength(200);")]
    [InlineData("[Precision(12, 4)] public decimal Litres { get; private set; }", "rules.RuleFor(s => s.Litres).PrecisionScale(12, 4, false);")]
    [InlineData("[MaxLength(100)] public string Name { get; private set; } = \"\";", "rules.RuleFor(s => s.Name).MaximumLength(Limit);")]
    public async Task A_rule_that_agrees_with_its_column_or_is_no_constant_is_not_RASK182(string member, string rule)
    {
        var source = Supplier("private static int Limit => 50; " + member, rule);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleFacetAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_rule_builder_that_is_not_FluentValidations_is_not_RASK182()
    {
        const string source = """
            using System.ComponentModel.DataAnnotations;
            namespace Shop;
            public sealed class Rules<T> { public Rules<T> RuleFor(System.Func<T, string> f) => this; public Rules<T> MaximumLength(int n) => this; }
            public sealed class Supplier
            {
                [MaxLength(100)] public string Name { get; set; } = "";
                static void Check(Rules<Supplier> rules) => rules.RuleFor(s => s.Name).MaximumLength(200);
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new FluentRuleFacetAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
