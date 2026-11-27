using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK146: a bare DateTime or DateTimeOffset held by the domain, which is a Moment or a Day.</summary>
public class DomainTimeAnalyzerTests
{
    private const string Domain = """
        using System;
        using Rask;
        using Rask.Cqrs;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;
        public sealed partial record FueledAt(DateTime Value) : Moment(Value);
        public sealed class Delivery : Aggregate<Guid>
        {
            private DateTimeOffset _seen;
            private Delivery() { }
            public DateTime Stamped { get; private set; }
            public DateTime? DueDate { get; private set; }
            public DateTimeOffset Logged { get; }
            public FueledAt Fueled { get; private set; } = null!;
            public DateOnly Day { get; private set; }
            public DateTime Computed => Stamped.AddDays(1);
            public static DateTime Epoch { get; } = DateTime.UnixEpoch;
            public DateTime DeletedAt { get; private set; }
        }
        public sealed partial record Window(DateTime From, DateTime? To) : Rask.Values.Range(From, To);
        public sealed record Delivered(Guid Id, DateTime At) : IEvent;
        public sealed class Report
        {
            public DateTime At { get; set; }
        }
        """;

    [Fact]
    public async Task Each_bare_time_the_domain_holds_is_a_RASK146_warning()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new DomainTimeAnalyzer(), Domain);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(["_seen", "Stamped", "DueDate", "Logged", "From", "To", "At"], flagged);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Contains("'Stamped' in 'Delivery' is a bare DateTime", diagnostics[1].GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Logged' in 'Delivery' is a bare DateTimeOffset", diagnostics[3].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_that_says_a_day_is_marked_so_the_fix_offers_a_Day_first()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new DomainTimeAnalyzer(), Domain);

        var due = diagnostics.Single(d => d.Flagged() == "DueDate");
        var stamped = diagnostics.Single(d => d.Flagged() == "Stamped");

        Assert.True(due.Properties.ContainsKey(DomainTimeAnalyzer.ReadsAsDay));
        Assert.False(stamped.Properties.ContainsKey(DomainTimeAnalyzer.ReadsAsDay));
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_gets_a_bare_time_in_the_domain_as_an_error()
    {
        var strict = StrictConfig.Severity("RASK146");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new DomainTimeAnalyzer(), Domain, options => options.WithSpecificDiagnosticOptions([new("RASK146", strict)]));

        Assert.Equal(ReportDiagnostic.Error, strict);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public async Task Rasks_own_types_are_the_frameworks_and_are_not_reported()
    {
        var source = Domain.Replace("namespace Shop;", "namespace Rask.Jobs;", StringComparison.Ordinal);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new DomainTimeAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
