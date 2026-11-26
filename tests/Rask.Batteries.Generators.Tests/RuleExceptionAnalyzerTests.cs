using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK184: an aggregate or an entity refusing with an exception no form can show.</summary>
public class RuleExceptionAnalyzerTests
{
    private const string Source = """
        using System;
        using Rask.Data;
        namespace Shop;
        public sealed class Order : Aggregate<Guid>
        {
            private Order() { }
            public int Quantity { get; private set; }
            public void Ship()
            {
                if (Quantity == 0) throw new InvalidOperationException("Nothing to ship.");
            }
            public void Set(int quantity)
            {
                if (quantity < 0) throw new ArgumentException("Cannot be negative.", nameof(quantity));
                if (quantity > 99) throw new ArgumentOutOfRangeException(nameof(quantity));
                Quantity = quantity;
            }
            public int Pick(int? amount) => amount ?? throw new InvalidOperationException("Pick an amount.");
            public sealed class Line : Entity<Guid>
            {
                private Line() { }
                public void Fail() => throw new InvalidOperationException("A line says no.");
            }
        }
        public static class Elsewhere
        {
            public static void Fail() => throw new InvalidOperationException("Not domain code.");
        }
        """;

    [Fact]
    public async Task An_aggregate_throwing_InvalidOperationException_or_ArgumentException_is_a_RASK184_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new RuleExceptionAnalyzer(), Source);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(
            [
                "new InvalidOperationException(\"Nothing to ship.\")",
                "new ArgumentException(\"Cannot be negative.\", nameof(quantity))",
                "new InvalidOperationException(\"Pick an amount.\")",
                "new InvalidOperationException(\"A line says no.\")",
            ],
            flagged);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        Assert.StartsWith("'InvalidOperationException' thrown in 'Order' reaches the person", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Line'", diagnostics[3].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_broken_invariant_thrown_as_UnreachableException_is_no_refusal_and_not_reported()
    {
        const string invariant = """
            using System;
            using System.Diagnostics;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public int Quantity { get; private set; }
                public void Ship()
                {
                    if (Quantity < 0) throw new UnreachableException("Set keeps Quantity at zero or more.");
                }
                public int Picked(int? amount) => amount ?? throw new UnreachableException("Pick always passes an amount.");
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new RuleExceptionAnalyzer(), invariant);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_an_aggregate_throwing_a_plain_exception()
    {
        var strict = StrictConfig.Severity("RASK184");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new RuleExceptionAnalyzer(), Source, options => options.WithSpecificDiagnosticOptions([new("RASK184", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }
}
