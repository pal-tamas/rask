using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK186: an Update whose lambda only runs an operation that has a generated entry.</summary>
public class UpdateThroughOperationAnalyzerTests
{
    // The generated surface, written by hand: the Update the model writes and the entry the operation generator gives.
    internal static string Source(string work, string traits = ", Versioned", string version = "int version, ") => $$"""
        using System;
        using System.CodeDom.Compiler;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed partial class Supply : Aggregate<Guid{{traits}}>
        {
            private Supply() { }
            public string Name { get; private set; } = "";
            public void Rename(string name) => Name = name;
            public Task Distribute(int litres) => Task.CompletedTask;
            public void Clear() => Name = "";

            [GeneratedCode("Rask.Operations", "1")]
            public static Task Rename(Guid id, {{version}}string name, CancellationToken cancellationToken = default) => Task.CompletedTask;

            [GeneratedCode("Rask.Operations", "1")]
            public static Task Distribute(Guid id, {{version}}int litres, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
        public static class SupplyModelExtensions
        {
            extension(Supply)
            {
                public static Task<Supply> Update(Guid id, Action<Supply> apply, int? version = null, DbContext? db = null, CancellationToken cancellationToken = default) =>
                    Task.FromResult<Supply>(null!);
                public static Task<Supply> Update(Guid id, Func<Supply, Task> apply, int? version = null, DbContext? db = null, CancellationToken cancellationToken = default) =>
                    Task.FromResult<Supply>(null!);
            }
        }
        public static class Page
        {
            public static async Task Run(Guid id, int read, int? maybe, string name, DbContext db, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    [Theory]
    [InlineData("await Supply.Update(id, s => s.Rename(name), version: read);", "Rename")]
    [InlineData("await Supply.Update(id, s => s.Rename(\"x\"), read);", "Rename")]
    [InlineData("await Supply.Update(id, async s => await s.Distribute(5), version: read, cancellationToken: token);", "Distribute")]
    [InlineData("await Supply.Update(id, s => { s.Rename(name.Trim()); }, version: read);", "Rename")]
    public async Task An_update_that_only_runs_an_operation_with_an_entry_is_RASK186(string work, string operation)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new UpdateThroughOperationAnalyzer(), source);

        var flagged = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Info, flagged.Severity);
        Assert.Equal(operation, flagged.Properties[UpdateThroughOperationAnalyzer.OperationProperty]);
        Assert.Contains("call 'Supply." + operation + "(id, …)'", flagged.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("await Supply.Update(id, s => s.Rename(name));")]
    [InlineData("await Supply.Update(id, s => s.Rename(name), version: maybe);")]
    [InlineData("await Supply.Update(id, s => s.Rename(name), version: read, db: db);")]
    [InlineData("var updated = await Supply.Update(id, s => s.Rename(name), version: read);")]
    [InlineData("await Supply.Update(id, s => s.Rename(s.Name + \"!\"), version: read);")]
    [InlineData("await Supply.Update(id, s => { s.Rename(name); s.Clear(); }, version: read);")]
    [InlineData("await Supply.Update(id, s => s.Clear(), version: read);")]
    public async Task An_update_that_does_more_or_reads_more_than_the_entry_is_not_RASK186(string work)
    {
        var source = Source(work);

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new UpdateThroughOperationAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_aggregate_with_no_version_is_told_to_call_its_entry_with_none()
    {
        var source = Source("await Supply.Update(id, s => s.Rename(name));", traits: "", version: "");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new UpdateThroughOperationAnalyzer(), source);

        Assert.False(Assert.Single(diagnostics).Properties.ContainsKey(UpdateThroughOperationAnalyzer.VersionProperty));
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_warned_about_an_update_that_bypasses_an_entry()
    {
        var strict = StrictConfig.Severity("RASK186");
        var source = Source("await Supply.Update(id, s => s.Rename(name), version: read);");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new UpdateThroughOperationAnalyzer(), source, options => options.WithSpecificDiagnosticOptions([new("RASK186", strict)]));

        Assert.Equal(ReportDiagnostic.Warn, strict);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(diagnostics).Severity);
    }
}
