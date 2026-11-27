using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK186 quick-fix: an Update that only runs an operation becomes the operation's generated entry.</summary>
public class OperationEntryCodeFixTests
{
    // The generated surface, written by hand: the Update the model writes and the entry the operation generator gives.
    private static string Source(string work, string traits = ", IVersioned", string version = "int version, ") => $$"""
        using System;
        using System.CodeDom.Compiler;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Shop;
        public sealed partial class Supply : Aggregate<Guid>{{traits}}
        {
            private Supply() { }
            public string Name { get; private set; } = "";
            public void Rename(string name, bool loud) => Name = loud ? name.ToUpperInvariant() : name;
            public Task Distribute(int litres) => Task.CompletedTask;

            [GeneratedCode("Rask.Operations", "1")]
            public static Task Rename(Guid id, {{version}}string name, bool loud, CancellationToken cancellationToken = default) => Task.CompletedTask;

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
            public static async Task Run(Guid id, int read, string name, CancellationToken token)
            {
                {{work}}
            }
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new UpdateThroughOperationAnalyzer(), new OperationEntryCodeFixProvider(), "RASK186", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire",
            "Microsoft.EntityFrameworkCore");

    [Theory]
    [InlineData("await Supply.Update(id, s => s.Rename(name, true), version: read);", "await Supply.Rename(id, read, name, true);")]
    [InlineData("await Supply.Update(id, s => s.Rename(name, false), read);", "await Supply.Rename(id, read, name, false);")]
    [InlineData("await Supply.Update(id, async s => await s.Distribute(5), version: read, cancellationToken: token);",
        "await Supply.Distribute(id, read, 5, cancellationToken: token);")]
    [InlineData("await Supply.Update(id: id, apply: s => { s.Rename(name.Trim(), true); }, version: read);", "await Supply.Rename(id, read, name.Trim(), true);")]
    public async Task The_RASK186_fix_calls_the_operations_entry(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Supply.Update(id", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK186_fix_passes_no_version_to_an_aggregate_that_has_none()
    {
        var source = Source("await Supply.Update(id, s => s.Rename(name, true));", traits: "", version: "");

        var fixed_ = await Fix(source);

        Assert.Contains("await Supply.Rename(id, name, true);", fixed_, StringComparison.Ordinal);
    }
}
