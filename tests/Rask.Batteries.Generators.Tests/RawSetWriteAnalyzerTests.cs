using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK187: EF Core's ExecuteUpdate/ExecuteDelete over an aggregate's rows, past what its own set write keeps.</summary>
public class RawSetWriteAnalyzerTests
{
    internal static string Source(string work) => $$"""
        using System;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed class Pairing : Aggregate<Guid, Versioned>
        {
            private Pairing() { }
            public int Pump { get; private set; }
            public bool Sent { get; private set; }
        }
        public sealed class Note
        {
            public int Id { get; set; }
            public int Pump { get; set; }
        }
        public sealed class ShopDb(DbContextOptions options) : DbContext(options)
        {
            public DbSet<Pairing> Pairings => Set<Pairing>();
            public DbSet<Note> Notes => Set<Note>();
        }
        public static class Page
        {
            public static async Task Run(ShopDb db, int pump, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("await db.Pairings.Where(p => p.Pump == pump).ExecuteDeleteAsync(token);", "DeleteWhere")]
    [InlineData("await db.Set<Pairing>().ExecuteDeleteAsync();", "DeleteWhere")]
    [InlineData("await db.Pairings.Where(p => p.Pump == pump).ExecuteUpdateAsync(s => s.SetProperty(p => p.Sent, true));", "UpdateWhere")]
    public async Task A_raw_set_write_on_an_aggregate_is_RASK187_with_its_fix(string work, string write)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new RawSetWriteAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, flagged.Severity);
        Assert.Equal(write, flagged.Properties[RawSetWriteAnalyzer.WriteProperty]);
        Assert.Contains("call 'Pairing." + write + "(…)'", flagged.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_synchronous_or_composed_set_write_is_reported_without_a_fix()
    {
        var source = Source("""
            db.Pairings.Where(p => p.Pump == pump).ExecuteDelete();
            await db.Pairings.Where(p => p.Pump == pump).OrderBy(p => p.Id).Take(5).ExecuteDeleteAsync();
            """);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new RawSetWriteAnalyzer(), source);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, static d => Assert.False(d.Properties.ContainsKey(RawSetWriteAnalyzer.WriteProperty)));
    }

    [Fact]
    public async Task A_set_write_on_a_table_that_is_no_aggregate_is_not_RASK187()
    {
        var source = Source("await db.Notes.Where(n => n.Pump == pump).ExecuteDeleteAsync(token);");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new RawSetWriteAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_a_raw_set_write()
    {
        var strict = StrictConfig.Severity("RASK187");
        var source = Source("await db.Pairings.ExecuteDeleteAsync();");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new RawSetWriteAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK187", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
