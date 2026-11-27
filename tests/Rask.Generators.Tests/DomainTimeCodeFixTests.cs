using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK146 quick-fixes: a bare DateTime in the domain is held as a Moment, or as a Day.</summary>
public class DomainTimeCodeFixTests
{
    private static string Source(string member) => $$"""
        using System;
        using Rask.Data;
        namespace Shop;
        public sealed class Delivery : Aggregate<Guid>
        {
            private Delivery() { }
            {{member}}
        }
        """;

    private static Task<string> Fix(string source, string key) =>
        CodeFixHarness.ApplyNamedAnalyzerFixAsync(
            new DomainTimeAnalyzer(), new DomainTimeCodeFixProvider(), "RASK146", source, key, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Fact]
    public async Task The_RASK146_Moment_fix_declares_a_moment_record_and_types_the_member_as_it()
    {
        var source = Source("public DateTime FueledAt { get; private set; }");

        var fixed_ = await Fix(source, "RASK146_Moment");

        Assert.Contains("public DeliveryFueledAt FueledAt { get; private set; }", fixed_, StringComparison.Ordinal);
        Assert.Contains("public sealed partial record DeliveryFueledAt(DateTime Value) : Moment(Value);", fixed_, StringComparison.Ordinal);
        Assert.Contains("using Rask.Values;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK146_Day_fix_declares_a_day_record_and_keeps_the_member_optional()
    {
        var source = Source("public DateTime? DueDate { get; private set; }");

        var fixed_ = await Fix(source, "RASK146_Day");

        Assert.Contains("public DeliveryDueDate? DueDate { get; private set; }", fixed_, StringComparison.Ordinal);
        Assert.Contains("public sealed partial record DeliveryDueDate(DateOnly Value) : Day(Value);", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK146_fix_is_withheld_where_a_type_of_that_name_exists_already()
    {
        var source = Source("public DateTime FueledAt { get; private set; }") + "\npublic sealed class DeliveryFueledAt;\n";

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new DomainTimeAnalyzer(), new DomainTimeCodeFixProvider(), "RASK146", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.False(offered);
    }
}
