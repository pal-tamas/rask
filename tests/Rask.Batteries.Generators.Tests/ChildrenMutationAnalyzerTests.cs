using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK180: Children&lt;TEntity, TSpec&gt;.Add and Remove are called only by the type that declares the children.</summary>
public class ChildrenMutationAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string inside, string elsewhere) =>
        ModelStateAnalyzerHarness.RunAsync(new ChildrenMutationAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Fuel;
            public sealed record PartSpec(Guid? Id);
            public sealed class SupplyPart : Entity<Guid>
            {
                public static SupplyPart For(PartSpec spec) => new();
            }
            public sealed class Supply : Aggregate<Guid>
            {
                public Children<SupplyPart, PartSpec> Parts { get; } = new();
                {{inside}}
                public sealed class Inner
                {
                    public void Take(Supply supply, SupplyPart part) => supply.Parts.Add(part);
                }
            }
            public static class Elsewhere
            {
                {{elsewhere}}
            }
            """);

    [Fact]
    public async Task Add_and_Remove_inside_the_aggregate_and_its_nested_types_are_fine()
    {
        var diagnostics = await Run(
            """
            public void Take(SupplyPart part) => Parts.Add(part);
            public bool Release(SupplyPart part) => this.Parts.Remove(part);
            public void TakeFrom(Supply other) => other.Parts.Add(other.Parts[0]);
            public void TakeAll(List<SupplyPart> parts) => parts.ForEach(Parts.Add);
            """,
            "");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Add_called_from_outside_the_aggregate_is_RASK180()
    {
        var diagnostic = Assert.Single(await Run("", "public static void Take(Supply s, SupplyPart p) => s.Parts.Add(p);"));

        Assert.Equal("RASK180", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("s.Parts.Add(p)", diagnostic.Flagged());
        Assert.Contains("'Add' changes the children of 'Supply' from outside it", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_handed_over_as_a_delegate_from_outside_is_RASK180()
    {
        var diagnostic = Assert.Single(await Run(
            "", "public static void Drop(Supply s, List<SupplyPart> p) => p.ForEach(x => s.Parts.Remove(x));"));

        Assert.Equal("RASK180", diagnostic.Id);
        Assert.Contains("'Remove'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Children_held_in_a_local_are_RASK180_even_inside_the_aggregate()
    {
        var diagnostic = Assert.Single(await Run("public void Take(SupplyPart p) { var parts = Parts; parts.Add(p); }", ""));

        Assert.Contains("read through a local", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_and_reads_from_outside_are_not_reported()
    {
        var diagnostics = await Run(
            "",
            """
            public static int Count(Supply s) => s.Parts.Count;
            public static void Change(Supply s, List<PartSpec> specs) => s.Parts.Sync(specs, x => x.Id, SupplyPart.For, (_, _) => { });
            """);

        Assert.Empty(diagnostics);
    }
}
