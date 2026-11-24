using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK143 and RASK144: a data annotation that does not do what it says on an aggregate or a model.</summary>
public class DataAnnotationAnalyzerTests
{
    // ---- RASK143 ----

    private const string Keys = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using Rask.Data;
        namespace Shop;
        public sealed class Order : Aggregate<Guid>
        {
            private Order() { }
            [Key] public string Number { get; private set; } = "";
            [MaxLength(10)] public string Note { get; private set; } = "";
        }
        public sealed class Line : Entity<int>
        {
            [Key] public int Position { get; private set; }
        }
        public sealed class Form
        {
            [Key] public int Id { get; set; }
        }
        """;

    [Fact]
    public async Task A_Key_on_an_aggregate_or_an_entity_is_a_RASK143_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new EntityKeyAttributeAnalyzer(), Keys);

        var messages = diagnostics.Select(d => d.GetMessage()).ToList();

        Assert.Equal(2, messages.Count);
        Assert.StartsWith("'Order.Number' is marked [Key], but 'Order' is keyed by the Id it inherits", messages[0], StringComparison.Ordinal);
        Assert.StartsWith("'Line.Position' is marked [Key]", messages[1], StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.Equal("Key", d.Flagged()));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_Key_on_an_entity()
    {
        var strict = StrictConfig.Severity("RASK143");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new EntityKeyAttributeAnalyzer(), Keys, options => options.WithSpecificDiagnosticOptions([new("RASK143", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }

    // ---- RASK144 ----

    private const string Compares = """
        using System.ComponentModel.DataAnnotations;
        namespace Shop;
        public class Account
        {
            public string Password { get; set; } = "";
        }
        public sealed class SignUp : Account
        {
            [Compare("Password")] public string Again { get; set; } = "";
            [Compare(nameof(Again))] public string Third { get; set; } = "";
            [Compare("password")] public string Wrong { get; set; } = "";
            [Compare("Missing")] public string Gone { get; set; } = "";
            [Compare("_field")] public string Field { get; set; } = "";
            private readonly string _field = "";
            public string Read() => _field;
        }
        """;

    [Fact]
    public async Task A_Compare_naming_no_property_of_the_model_is_a_RASK144_suggestion()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new CompareTargetAnalyzer(), Compares);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(["Compare(\"password\")", "Compare(\"Missing\")", "Compare(\"_field\")"], flagged);
        Assert.StartsWith("[Compare(\"Missing\")] on 'SignUp.Gone' names no property of 'SignUp'", diagnostics[1].GetMessage(), StringComparison.Ordinal);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_Compare_naming_no_property()
    {
        var strict = StrictConfig.Severity("RASK144");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new CompareTargetAnalyzer(), Compares, options => options.WithSpecificDiagnosticOptions([new("RASK144", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }
}
