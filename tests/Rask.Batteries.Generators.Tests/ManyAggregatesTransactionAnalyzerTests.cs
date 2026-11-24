using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK178: a transaction that writes two different aggregates without saying why.</summary>
public class ManyAggregatesTransactionAnalyzerTests
{
    // The shape the generated writes take: extension blocks over the aggregates, beside their own methods.
    private static string Transaction(string work) => $$"""
        using System;
        using System.Data;
        using System.Threading.Tasks;
        using Rask.Data;
        namespace Shop;
        public sealed partial class Tank : Aggregate<Guid>
        {
            private Tank() { }
            public void Fill(int litres) { }
            private static Tank Open(string name, int code) => new();
            public static Task<int> Probe(Guid id) => Task.FromResult(0);
        }
        // What the operation generator writes into the aggregate: an entry beside an operation and a factory.
        public sealed partial class Tank
        {
            public static Task Fill(Guid id, int litres) => Task.CompletedTask;
            public static Task<Guid> Open(string name) => Task.FromResult(Guid.Empty);
        }
        public sealed class Pump : Aggregate<Guid>
        {
            private Pump() { }
        }
        public static class TankModelExtensions
        {
            extension(Tank)
            {
                public static Task<Tank?> Find(Guid id) => Task.FromResult<Tank?>(null);
                public static Task<int> Count() => Task.FromResult(0);
                public static Task Delete(Guid id) => Task.CompletedTask;
            }
            extension(Tank tank)
            {
                public Task Save() => Task.CompletedTask;
            }
        }
        public static class PumpModelExtensions
        {
            extension(Pump)
            {
                public static Task<Pump?> Find(Guid id) => Task.FromResult<Pump?>(null);
                public static Task<int> Count() => Task.FromResult(0);
            }
            extension(Pump pump)
            {
                public Task Save() => Task.CompletedTask;
            }
        }
        public static class Moves
        {
            public static async Task Run(Guid id, Tank tank, Pump pump)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("await Db.Transaction(async () => { await tank.Save(); await pump.Save(); });")]
    [InlineData("await Db.Transaction(async () => { await Tank.Fill(id, 5); await pump.Save(); });")]
    [InlineData("await Db.Transaction(async () => { await Tank.Open(\"a\"); await pump.Save(); });")]
    [InlineData("await Db.Transaction(async () => { await Tank.Delete(id); await Task.Run(async () => await pump.Save()); });")]
    [InlineData("var n = await Db.Transaction(async () => { await tank.Save(); await pump.Save(); return 1; }, IsolationLevel.Serializable);")]
    public async Task Two_different_aggregates_written_in_one_transaction_without_a_reason_are_RASK178(string work)
    {
        var source = Transaction(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ManyAggregatesTransactionAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.StartsWith("Db.Transaction(", flagged.Flagged(), StringComparison.Ordinal);
        Assert.Contains("writes 'Tank' and 'Pump'", flagged.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Info, flagged.Severity);
    }

    [Theory]
    [InlineData("await Db.Transaction(async () => { await tank.Save(); await Tank.Fill(id, 5); await tank.Save(); });")]
    [InlineData("await Db.Transaction(async () => { await tank.Save(); await pump.Save(); }, manyAggregates: \"The filling and its pump are only valid together\");")]
    [InlineData("await Db.Transaction(async () => { await Tank.Count(); await Tank.Probe(id); await Pump.Find(id); await tank.Save(); });")]
    [InlineData("await tank.Save(); await pump.Save();")]
    public async Task One_aggregate_a_stated_reason_or_reads_only_are_not_RASK178(string work)
    {
        var source = Transaction(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ManyAggregatesTransactionAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_transaction_of_two_aggregates()
    {
        var strict = StrictConfig.Severity("RASK178");
        var source = Transaction("await Db.Transaction(async () => { await tank.Save(); await pump.Save(); });");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new ManyAggregatesTransactionAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK178", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
