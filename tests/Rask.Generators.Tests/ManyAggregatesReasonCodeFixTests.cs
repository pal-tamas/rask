using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK178 quick-fix: a transaction that writes two aggregates gets a reason to fill in.</summary>
public class ManyAggregatesReasonCodeFixTests
{
    private static string Source(string work) => $$"""
        using System;
        using System.Data;
        using System.Threading.Tasks;
        using Rask.Data;
        namespace Shop;
        public sealed class Tank : Aggregate<Guid> { private Tank() { } }
        public sealed class Pump : Aggregate<Guid> { private Pump() { } }
        public static class Writes
        {
            extension(Tank tank) { public Task Save() => Task.CompletedTask; }
            extension(Pump pump) { public Task Save() => Task.CompletedTask; }
        }
        public static class Moves
        {
            public static async Task Run(Tank tank, Pump pump)
            {
                {{work}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ManyAggregatesTransactionAnalyzer(), new ManyAggregatesReasonCodeFixProvider(), "RASK178", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Theory]
    [InlineData(
        "await Db.Transaction(async () => { await tank.Save(); await pump.Save(); });",
        "await Db.Transaction(async () => { await tank.Save(); await pump.Save(); }, manyAggregates: \"TODO: why\");")]
    [InlineData(
        "await Db.Transaction(async () => { await tank.Save(); await pump.Save(); }, IsolationLevel.Serializable);",
        "await Db.Transaction(async () => { await tank.Save(); await pump.Save(); }, manyAggregates: \"TODO: why\", IsolationLevel.Serializable);")]
    public async Task The_RASK178_fix_states_a_reason_right_after_the_work(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
    }
}
