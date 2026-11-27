using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK192: an aggregate's children held by hand, where one HasMany&lt;X&gt; says the same.</summary>
public class HandListedChildrenAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string members, string owner = "Aggregate<Guid>") =>
        ModelStateAnalyzerHarness.RunAsync(new HandListedChildrenAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Armory;
            public sealed class Pistol : Entity<Guid>;
            public sealed class Gallery : Aggregate<Guid>;
            public sealed class Rack : {{owner}}
            {
                {{members}}
            }
            """);

    [Theory]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols;")]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyCollection<Pistol> Pistols => _pistols.AsReadOnly();")]
    [InlineData("private readonly List<Pistol> _pistols = []; public IEnumerable<Pistol> Pistols { get { return _pistols; } }")]
    [InlineData("public List<Pistol> Pistols { get; private set; } = [];")]
    [InlineData("public ICollection<Pistol> Pistols { get; } = new List<Pistol>();")]
    public async Task A_child_list_held_by_hand_on_an_aggregate_is_RASK192(string members)
    {
        var diagnostics = await Run(members);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RASK192", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("'Rack.Pistols' holds its 'Pistol' children by hand — declare 'public HasMany<Pistol> Pistols { get; } = new();'",
            diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public HasMany<Pistol> Pistols { get; } = new();")]
    [InlineData("private readonly List<Gallery> _galleries = []; public IReadOnlyList<Gallery> Galleries => _galleries;")]
    [InlineData("public IReadOnlyList<Pistol> Pistols { get; } = [];")]
    [InlineData("private readonly List<string> _names = []; public IReadOnlyList<string> Names => _names;")]
    public async Task A_HasMany_a_list_of_other_aggregates_and_lists_of_no_children_are_not_RASK192(string members)
    {
        var diagnostics = await Run(members);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_child_entity_holding_a_list_of_its_own_is_not_RASK192()
    {
        var diagnostics = await Run("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols;", "Entity<Guid>");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_is_refused_a_child_list_held_by_hand()
    {
        var strict = StrictConfig.Severity("RASK192");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new HandListedChildrenAnalyzer(),
            """
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Armory;
            public sealed class Pistol : Entity<Guid>;
            public sealed class Rack : Aggregate<Guid>
            {
                public List<Pistol> Pistols { get; private set; } = [];
            }
            """,
            options => options.WithSpecificDiagnosticOptions([new("RASK192", strict)]));

        Assert.Equal(ReportDiagnostic.Error, strict);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(diagnostics).Severity);
    }
}
