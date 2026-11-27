using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK145: an event raised about an entity just made reads a key the database produces only on insert.</summary>
public class StoreKeyEventAnalyzerTests
{
    private const string Reported = """
        using System;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Cqrs;
        using Rask.Data;
        namespace Shop;
        public readonly partial record struct PeriodId(int Value);
        public sealed record Placed(int Order) : IEvent;
        public sealed record Opened(PeriodId Period) : IEvent;
        public sealed record LineAdded(int Order, int Line) : IEvent;
        public sealed record Tagged(Guid Tag) : IEvent;
        public sealed class Order : Aggregate<int>
        {
            private readonly System.Collections.Generic.List<Line> _lines = [];
            private Order() { }
            public static Order Place()
            {
                var order = new Order();
                order.Raise(new Placed(order.Id));
                return order;
            }
            public void Add(int quantity)
            {
                var line = Line.Of(quantity);
                _lines.Add(line);
                Raise(new LineAdded(Id, line.Id));
            }
        }
        public sealed class Line : Entity<int>
        {
            private Line() { }
            public int Quantity { get; private set; }
            public static Line Of(int quantity) => new() { Quantity = quantity };
        }
        public sealed class Period : Aggregate<PeriodId>
        {
            public Period(string name)
            {
                Name = name;
                Raise(new Opened(this.Id));
            }
            public string Name { get; private set; }
        }
        public sealed class Tag : Aggregate<Guid>
        {
            private Tag() { }
            public static Tag Make()
            {
                Tag tag = new();
                tag.Raise(new Tagged(tag.Id));
                return tag;
            }
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Property(t => t.Id).ValueGeneratedOnAdd();
        }
        """;

    [Fact]
    public async Task An_event_reading_the_store_key_of_an_entity_just_made_is_a_RASK145_warning()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new StoreKeyEventAnalyzer(), Reported);

        var flagged = diagnostics.Select(d => d.Flagged()).ToList();

        Assert.Equal(["order.Id", "line.Id", "this.Id", "tag.Id"], flagged);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
        Assert.Equal(
            "The event raised in 'Order' reads 'order.Id', which the database produces only on insert: raised before the "
            + "first save, it carries 0 — give 'Order' a client-generated key (a typed id over a Guid, made with Create())",
            diagnostics[0].GetMessage());
        Assert.Contains("give 'Line'", diagnostics[1].GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_client_key_a_never_generated_key_and_a_stored_aggregates_operation_are_not_reported()
    {
        const string source = """
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Cqrs;
            using Rask.Data;
            namespace Shop;
            public readonly partial record struct OrderId(Guid Value);
            public sealed record Placed(OrderId Order) : IEvent;
            public sealed record Shipped(int Order) : IEvent;
            public sealed record Numbered(int Invoice) : IEvent;
            public sealed class Order : Aggregate<OrderId>
            {
                private Order() { }
                public static Order Place()
                {
                    var order = new Order { Id = new OrderId(Guid.CreateVersion7()) };
                    order.Raise(new Placed(order.Id));
                    return order;
                }
            }
            public sealed class Shipment : Aggregate<int>
            {
                private Shipment() { }
                public void Ship() => Raise(new Shipped(Id));
                public static void Again(Shipment stored) => stored.Raise(new Shipped(stored.Id));
            }
            public sealed class Invoice : Aggregate<int>
            {
                private Invoice() { }
                public static Invoice Numbered(int number)
                {
                    var invoice = new Invoice { Id = number };
                    invoice.Raise(new Numbered(invoice.Id));
                    return invoice;
                }
                public static void Configure(EntityTypeBuilder<Invoice> b) => b.Property(i => i.Id).ValueGeneratedNever();
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new StoreKeyEventAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_app_that_sets_RaskStrict_gets_an_error_for_an_event_reading_a_store_key()
    {
        var strict = StrictConfig.Severity("RASK145");

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(
            new StoreKeyEventAnalyzer(), Reported, options => options.WithSpecificDiagnosticOptions([new("RASK145", strict)]));

        Assert.Equal(ReportDiagnostic.Error, strict);
        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }
}
