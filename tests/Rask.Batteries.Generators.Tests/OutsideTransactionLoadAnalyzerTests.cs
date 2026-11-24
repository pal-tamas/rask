using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK185: an aggregate loaded before the Db.Transaction that changes it.</summary>
public class OutsideTransactionLoadAnalyzerTests
{
    private static string Source(string work) => $$"""
        using System;
        using System.Threading.Tasks;
        using Rask.Data;
        namespace Shop;
        public sealed class Tank : Aggregate<Guid>
        {
            private Tank() { }
            public int Level { get; private set; }
            public void Fill(int litres) => Level += litres;
        }
        public static class TankModelExtensions
        {
            extension(Tank)
            {
                public static Task<Tank?> Find(Guid id) => Task.FromResult<Tank?>(null);
            }
            extension(Tank tank)
            {
                public Task Save() => Task.CompletedTask;
            }
        }
        public static class Moves
        {
            public static async Task Run(Guid id, Tank given)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("var tank = (await Tank.Find(id))!; await Db.Transaction(async () => { tank.Fill(5); await tank.Save(); });", true)]
    [InlineData("var tank = (await Tank.Find(id))!; await Db.Transaction(() => tank.Save());", false)]
    [InlineData("var tank = (await Tank.Find(id))!; var n = await Db.Transaction(async () => { await tank.Save(); return tank.Level; });", true)]
    [InlineData("var tank = (await Tank.Find(id))!; await Db.Transaction(async () => await tank.Save()); Console.WriteLine(tank.Level);", false)]
    [InlineData("var tank = (await Tank.Find(id))!; Console.WriteLine(tank.Level); await Db.Transaction(async () => await tank.Save());", false)]
    [InlineData("Tank tank = null!, other = null!; await Db.Transaction(async () => await tank.Save());", false)]
    public async Task An_aggregate_read_before_the_transaction_that_changes_it_is_RASK185(string work, bool movable)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new OutsideTransactionLoadAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.Equal("tank", flagged.Flagged());
        Assert.StartsWith("'tank' is a 'Tank' read before Db.Transaction", flagged.GetMessage(), StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Info, flagged.Severity);
        Assert.Equal(movable, flagged.Properties.ContainsKey(OutsideTransactionLoadAnalyzer.MovableProperty));
    }

    [Theory]
    [InlineData("await Db.Transaction(async () => { var tank = (await Tank.Find(id))!; tank.Fill(5); await tank.Save(); });")]
    [InlineData("Tank? tank = null; await Db.Transaction(async () => { tank = await Tank.Find(id); await tank!.Save(); });")]
    [InlineData("await Db.Transaction(async () => { given.Fill(1); await given.Save(); });")]
    [InlineData("var tank = (await Tank.Find(id))!; tank.Fill(5); await tank.Save();")]
    [InlineData("await Db.Transaction(async () => { var tank = (await Tank.Find(id))!; await Db.Transaction(async () => await tank.Save()); });")]
    public async Task An_aggregate_loaded_inside_the_block_or_handed_in_is_not_RASK185(string work)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new OutsideTransactionLoadAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_an_aggregate_loaded_outside_its_transaction()
    {
        var strict = StrictConfig.Severity("RASK185");
        var source = Source("var tank = (await Tank.Find(id))!; await Db.Transaction(async () => await tank.Save());");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new OutsideTransactionLoadAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK185", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
