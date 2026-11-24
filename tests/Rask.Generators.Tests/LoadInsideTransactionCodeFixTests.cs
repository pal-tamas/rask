using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK185 quick-fix: an aggregate loaded before its transaction is loaded inside it.</summary>
public class LoadInsideTransactionCodeFixTests
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
            public static async Task<int> Run(Guid id)
            {
                {{work}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new OutsideTransactionLoadAnalyzer(), new LoadInsideTransactionCodeFixProvider(), "RASK185", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    private static string Squashed(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public async Task The_RASK185_fix_moves_the_load_to_the_top_of_the_block()
    {
        var source = Source("""
                    var tank = (await Tank.Find(id))!;
                    await Db.Transaction(async () =>
                    {
                        tank.Fill(5);
                        await tank.Save();
                    });
                    return 0;
            """);

        var fixed_ = await Fix(source);

        Assert.Contains(
            "{ await Db.Transaction(async () => { var tank = (await Tank.Find(id))!; tank.Fill(5); await tank.Save(); }); return 0; }",
            Squashed(fixed_), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK185_fix_turns_an_expression_body_that_returns_into_a_block_that_returns()
    {
        var source = Source("""
                    var tank = (await Tank.Find(id))!;
                    return await Db.Transaction(async () => tank.Level);
            """);

        var fixed_ = await Fix(source);

        Assert.Contains(
            "{ return await Db.Transaction(async () => { var tank = (await Tank.Find(id))!; return tank.Level; }); }",
            Squashed(fixed_), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK185_fix_is_withheld_when_the_local_is_read_after_the_block()
    {
        var source = Source("""
                    var tank = (await Tank.Find(id))!;
                    await Db.Transaction(async () => await tank.Save());
                    return tank.Level;
            """);

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new OutsideTransactionLoadAnalyzer(), new LoadInsideTransactionCodeFixProvider(), "RASK185", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.False(offered);
    }
}
