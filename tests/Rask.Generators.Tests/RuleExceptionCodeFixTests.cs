using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK184 quick-fix: an aggregate's plain exception becomes a refusal a form shows.</summary>
public class RuleExceptionCodeFixTests
{
    private static string Source(string member, string usings = "using Rask.Data;\nusing Rask.Wire;") => $$"""
        using System;
        {{usings}}
        namespace Shop;
        public sealed class Order : Rask.Data.Aggregate<Guid>
        {
            private Order() { }
            public int Quantity { get; private set; }
            {{member}}
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new RuleExceptionAnalyzer(), new RuleExceptionCodeFixProvider(), "RASK184", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Theory]
    [InlineData("public void Ship() { if (Quantity == 0) throw new InvalidOperationException(\"Nothing to ship.\"); }",
        "Refuse.When(Quantity == 0, \"Nothing to ship.\");")]
    [InlineData("public void Ship() { if (Quantity == 0) { throw new InvalidOperationException(\"Nothing to ship.\"); } }",
        "Refuse.When(Quantity == 0, \"Nothing to ship.\");")]
    [InlineData("public void Ship(bool paid) { if (!paid) throw new InvalidOperationException(\"Pay first.\"); }",
        "Refuse.Unless(paid, \"Pay first.\");")]
    [InlineData("public void Ship(int a, int b) { if (!(a < b)) throw new InvalidOperationException(\"Out of order.\"); }",
        "Refuse.Unless(a < b, \"Out of order.\");")]
    [InlineData("public void Set(int quantity) { if (quantity < 0) throw new ArgumentException(\"Cannot be negative.\", nameof(quantity)); }",
        "Refuse.When(quantity < 0, () => quantity, \"Cannot be negative.\");")]
    [InlineData("public int Pick(int? amount) => amount ?? throw new InvalidOperationException(\"Pick an amount.\");",
        "amount ?? throw new RaskValidationException(\"Pick an amount.\");")]
    [InlineData("public void Ship() { if (Quantity == 0) throw new InvalidOperationException(\"Nothing.\"); else Quantity--; }",
        "throw new RaskValidationException(\"Nothing.\"); else")]
    [InlineData("public void Ship(Exception e) { throw new InvalidOperationException(\"Failed.\", e); }",
        "throw new RaskValidationException(\"Failed.\", e);")]
    public async Task The_RASK184_fix_refuses_with_the_same_sentence(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception(\"", fixed_.Replace("RaskValidationException(\"", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK184_fix_adds_the_using_its_refusal_needs()
    {
        var source = Source("public void Ship() { if (Quantity == 0) throw new InvalidOperationException(\"Nothing to ship.\"); }", usings: "");

        var fixed_ = await Fix(source);

        Assert.Contains("using Rask.Data;", fixed_, StringComparison.Ordinal);
        Assert.Contains("Refuse.When(Quantity == 0, \"Nothing to ship.\");", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public void Ship() => throw new InvalidOperationException();")]
    [InlineData("public void Ship(object o) => throw new ArgumentException(\"Bad.\", o.ToString());")]
    public async Task The_RASK184_fix_is_withheld_without_a_sentence_to_show(string written)
    {
        var source = Source(written);

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new RuleExceptionAnalyzer(), new RuleExceptionCodeFixProvider(), "RASK184", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.False(offered);
    }
}
