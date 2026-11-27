using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK187 quick-fix: a raw ExecuteUpdate/ExecuteDelete on an aggregate becomes its UpdateWhere/DeleteWhere.</summary>
public class SetWriteCodeFixTests
{
    private static string Source(string work) => $$"""
        using System;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed class Pairing : Aggregate<Guid>, IVersioned
        {
            private Pairing() { }
            public int Pump { get; private set; }
            public bool Sent { get; private set; }
        }
        public sealed class ShopDb(DbContextOptions options) : DbContext(options)
        {
            public DbSet<Pairing> Pairings => Set<Pairing>();
        }
        public static class Page
        {
            public static async Task Run(ShopDb db, int pump, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new RawSetWriteAnalyzer(), new SetWriteCodeFixProvider(), "RASK187", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire",
            "Microsoft.EntityFrameworkCore");

    [Theory]
    [InlineData("await db.Pairings.Where(p => p.Pump == pump).ExecuteDeleteAsync(token);",
        "await Pairing.DeleteWhere(p => p.Pump == pump, cancellationToken: token);")]
    [InlineData("await db.Set<Pairing>().ExecuteDeleteAsync();", "await Pairing.DeleteWhere(_ => true);")]
    [InlineData("await db.Pairings.Where(p => p.Pump == pump).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sent, true), token);",
        "await Pairing.UpdateWhere(p => p.Pump == pump, s => s.SetProperty(p => p.Sent, true), cancellationToken: token);")]
    public async Task The_RASK187_fix_calls_the_aggregates_own_set_write(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Execute", fixed_, StringComparison.Ordinal);
    }
}
