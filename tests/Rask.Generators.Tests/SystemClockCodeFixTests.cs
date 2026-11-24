using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK140 quick-fix: the machine's clock in domain code becomes Rask's <c>Clock</c>.</summary>
public class SystemClockCodeFixTests
{
    private static string Source(string member, string usings = "using Rask;") => $$"""
        using System;
        {{usings}}
        using Rask.Data;
        namespace Shop;
        public sealed class Delivery : Aggregate<Guid>
        {
            private Delivery() { }
            public DateTime Stamped { get; private set; }
            public DateTimeOffset At { get; private set; }
            {{member}}
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new SystemClockAnalyzer(), new SystemClockCodeFixProvider(), "RASK140", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Theory]
    [InlineData("public void Stamp() => Stamped = DateTime.Now;", "Stamped = Clock.Wall;")]
    [InlineData("public DateTime Day() => DateTime.Today;", "=> Clock.Wall.Date;")]
    [InlineData("public DateOnly Day() => DateOnly.FromDateTime(DateTime.Today);", "=> Clock.Today;")]
    [InlineData("public void Stamp() => Stamped = DateTime.UtcNow;", "Stamped = Clock.Now.UtcDateTime;")]
    [InlineData("public void Stamp() => At = DateTimeOffset.Now;", "At = Clock.Now;")]
    [InlineData("public void Stamp() => At = DateTimeOffset.UtcNow;", "At = Clock.Now;")]
    [InlineData("public DateTime Local() => Stamped.ToLocalTime();", "=> TimeZoneInfo.ConvertTimeFromUtc(Stamped, Clock.Zone);")]
    [InlineData("public DateTimeOffset Local() => At.ToLocalTime();", "=> TimeZoneInfo.ConvertTime(At, Clock.Zone);")]
    public async Task The_RASK140_fix_reads_Rasks_clock_in_the_same_type(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK140_fix_qualifies_Clock_where_Rask_is_not_imported()
    {
        var source = Source("public void Stamp() => Stamped = DateTime.Now;", usings: "");

        var fixed_ = await Fix(source);

        Assert.Contains("Stamped = global::Rask.Clock.Wall;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK140_fix_is_withheld_for_a_conditional_ToLocalTime()
    {
        var source = Source("public DateTime? Local(DateTime? at) => at?.ToLocalTime();");

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new SystemClockAnalyzer(), new SystemClockCodeFixProvider(), "RASK140", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.False(offered);
    }
}
