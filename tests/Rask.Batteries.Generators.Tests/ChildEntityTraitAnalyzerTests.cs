using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK113: a child entity keeps only its own timestamps; every other trait is its aggregate root's.</summary>
public class ChildEntityTraitAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string lineTraits) =>
        ModelStateAnalyzerHarness.RunAsync(new ChildEntityTraitAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, ISoftDelete, IPerTenant, IBroadcast
            {
                private readonly List<OrderLine> _lines = [];
                public IReadOnlyList<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid>{{lineTraits}}
            {
                public string Sku { get; private set; } = "";
            }
            """);

    [Theory]
    [InlineData(", IPerTenant", "'IPerTenant'")]
    [InlineData(", IPerTenant<int>", "'IPerTenant<int>'")]
    [InlineData(", IFormModel", "'IFormModel'")]
    [InlineData(", ICreateFormModel", "'ICreateFormModel'")]
    [InlineData(", IDeletable", "'IDeletable'")]
    [InlineData(", ITimestamps, IBroadcast", "'IBroadcast'")]
    public async Task A_child_that_implements_a_root_trait_is_RASK113(string traits, string named)
    {
        var diagnostic = Assert.Single(await Run(traits));

        Assert.Equal("RASK113", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'OrderLine' is a child entity, so it cannot implement " + named, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_root_trait_on_the_child_is_named_in_one_report()
    {
        var diagnostic = Assert.Single(await Run(", IDeletable, IBroadcast"));

        Assert.Contains("'IDeletable', 'IBroadcast'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(", ICreated")]
    [InlineData(", IUpdated")]
    [InlineData(", ITimestamps")]
    public async Task A_child_with_its_own_timestamps_and_a_root_with_every_trait_are_fine(string traits) =>
        Assert.Empty(await Run(traits));

    [Fact]
    public async Task A_root_trait_on_a_childs_base_is_reported_on_the_base_alone()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ChildEntityTraitAnalyzer(), """
            using System;
            using Rask.Data;
            namespace Shop;
            public abstract class Line : Entity<Guid>, IBroadcast;
            public sealed class OrderLine : Line;
            public sealed class InvoiceLine : Line;
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Line' is a child entity", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_type_that_is_not_an_entity_is_not_this_rules_business()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new ChildEntityTraitAnalyzer(), """
            using Rask.Data;
            namespace Shop;
            public sealed class Settings : IPerTenant, IBroadcast;
            """);

        Assert.Empty(diagnostics);
    }
}
