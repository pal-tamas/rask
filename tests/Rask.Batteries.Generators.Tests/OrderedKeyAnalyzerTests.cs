using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK148: an entity keyed by a Guid that two rows made in one millisecond do not sort by.</summary>
public class OrderedKeyAnalyzerTests
{
    [Fact]
    public async Task A_key_set_to_CreateVersion7_or_NewGuid_is_a_RASK148_suggestion_naming_the_ordered_one()
    {
        const string source = """
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly partial record struct OrderId(Guid Value)
            {
                public static OrderId New() => new(Rask.OrderedGuid.New());
            }
            public sealed class Order : Aggregate<OrderId>
            {
                private Order() { }
                public static Order Place() => new() { Id = new OrderId(Guid.CreateVersion7()) };
            }
            public sealed class Line : Entity<Guid>
            {
                public Line() => Id = Guid.NewGuid();
            }
            public sealed class Note : Aggregate<Guid>
            {
                private Note() { }
                public static Note Write() => new() { Id = Guid.CreateVersion7() };
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new OrderedKeyAnalyzer(), source);

        Assert.Equal(["Guid.CreateVersion7()", "Guid.NewGuid()", "Guid.CreateVersion7()"], diagnostics.Select(d => d.Flagged()));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Info, d.Severity));
        Assert.Equal(
            "'Order' keys 'Id' with 'Guid.CreateVersion7()', which two rows made in one millisecond do not sort by — use 'OrderId.New()'",
            diagnostics[0].GetMessage());
        Assert.EndsWith("use 'OrderedGuid.New()'", diagnostics[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_made_by_New_and_a_Guid_that_is_not_the_key_are_not_reported()
    {
        const string source = """
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly partial record struct OrderId(Guid Value)
            {
                public static OrderId New() => new(Rask.OrderedGuid.New());
            }
            public sealed class Order : Aggregate<OrderId>
            {
                private Order() { }
                public Guid Ticket { get; private set; }
                public static Order Place() => new() { Id = OrderId.New(), Ticket = Guid.NewGuid() };
            }
            public sealed class Note : Aggregate<Guid>
            {
                private Note() { }
                public static Note Write() => new() { Id = Rask.OrderedGuid.New() };
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new OrderedKeyAnalyzer(), source);

        Assert.Empty(diagnostics);
    }
}
