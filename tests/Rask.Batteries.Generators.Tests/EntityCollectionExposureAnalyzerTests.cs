using Microsoft.CodeAnalysis;
using Rask.Data.Generators.Analyzers;

namespace Rask.Data.Generators.Tests;

/// <summary>RASK081: an entity exposes a mutable collection of other entities.</summary>
public class EntityCollectionExposureAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string members) =>
        ModelStateAnalyzerHarness.RunAsync(new EntityCollectionExposureAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Collections.ObjectModel;
            using Rask.Data;
            namespace Shop;
            public sealed class OrderLine : Model<Guid> { }
            public sealed record Money(decimal Amount, string Currency) : IValueObject;
            public sealed class Order : Model<Guid>
            {
                {{members}}
            }
            """);

    [Theory]
    [InlineData("public List<OrderLine> Lines { get; private set; } = new();", "List<OrderLine>")]
    [InlineData("public IList<OrderLine> Lines { get; private set; } = [];", "IList<OrderLine>")]
    [InlineData("public ICollection<OrderLine> Lines { get; private set; } = [];", "ICollection<OrderLine>")]
    [InlineData("public HashSet<OrderLine> Lines { get; } = [];", "HashSet<OrderLine>")]
    [InlineData("public ISet<OrderLine> Lines { get; } = new HashSet<OrderLine>();", "ISet<OrderLine>")]
    [InlineData("public Collection<OrderLine> Lines { get; } = [];", "Collection<OrderLine>")]
    public async Task A_mutable_collection_of_entities_is_reported_at_its_type(string member, string type)
    {
        var diagnostic = Assert.Single(await Run(member));

        Assert.Equal("RASK081", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(type, diagnostic.Flagged());
        Assert.Contains($"'Order.Lines' exposes a mutable '{type}' of 'OrderLine'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'IReadOnlyCollection<OrderLine>'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private readonly List<OrderLine> _lines = []; public IReadOnlyCollection<OrderLine> Lines => _lines;")]
    [InlineData("private readonly List<OrderLine> _lines = []; public IReadOnlyList<OrderLine> Lines => _lines;")]
    [InlineData("private readonly List<OrderLine> _lines = []; public IEnumerable<OrderLine> Lines => _lines;")]
    [InlineData("public ReadOnlyCollection<OrderLine> Lines { get; } = new([]);")]
    [InlineData("public ImmutableList<OrderLine> Lines { get; } = [];")]
    public async Task A_read_only_exposure_is_not_reported(string member) =>
        Assert.Empty(await Run(member));

    [Fact]
    public async Task A_list_of_strings_on_an_entity_is_not_reported() =>
        Assert.Empty(await Run("public List<string> Tags { get; private set; } = [];"));

    [Fact]
    public async Task A_list_of_value_objects_on_an_entity_is_not_reported() =>
        Assert.Empty(await Run("public List<Money> Payments { get; private set; } = [];"));

    [Fact]
    public async Task A_non_public_collection_is_not_reported() =>
        Assert.Empty(await Run("""
            internal List<OrderLine> Lines { get; } = [];
            private List<OrderLine> Others { get; } = [];
            """));

    [Fact]
    public async Task A_single_entity_navigation_is_not_reported() =>
        Assert.Empty(await Run("public OrderLine? First { get; private set; }"));

    [Fact]
    public async Task A_collection_on_a_type_that_is_not_an_entity_is_not_reported() =>
        Assert.Empty(await ModelStateAnalyzerHarness.RunAsync(new EntityCollectionExposureAnalyzer(), """
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class OrderLine : Model<Guid> { }
            public sealed class OrderForm { public List<OrderLine> Lines { get; set; } = []; }
            """));

    [Fact]
    public async Task A_collection_on_an_abstract_entity_base_is_reported()
    {
        var diagnostic = Assert.Single(await ModelStateAnalyzerHarness.RunAsync(new EntityCollectionExposureAnalyzer(), """
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Note : Model<Guid> { }
            public abstract class Annotated : Model<Guid> { public List<Note> Notes { get; } = []; }
            public sealed class Order : Annotated { }
            """));

        Assert.Contains("'Annotated.Notes'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
