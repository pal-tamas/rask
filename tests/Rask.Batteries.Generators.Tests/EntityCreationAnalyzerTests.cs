using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK107: an aggregate is made inside its own type, a child inside its own type or its holder.</summary>
public class EntityCreationAnalyzerTests
{
    private static Task<IReadOnlyList<Diagnostic>> Run(string elsewhere) =>
        ModelStateAnalyzerHarness.RunAsync(new EntityCreationAnalyzer(), $$"""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                public IReadOnlyList<OrderLine> Lines => _lines;
                public static Order Place() => new();
                public void Add() => _lines.Add(new OrderLine());
            }
            public sealed class OrderLine : Entity<Guid>
            {
                public static OrderLine Of() => new();
            }
            public static class Elsewhere
            {
                {{elsewhere}}
            }
            """);

    [Fact]
    public async Task An_aggregate_made_with_new_outside_its_type_is_RASK107()
    {
        var diagnostic = Assert.Single(await Run("public static Order Make() => new Order();"));

        Assert.Equal("RASK107", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Order' is an aggregate, so it is made only inside 'Order'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_made_with_new_outside_its_type_and_its_holder_is_RASK107()
    {
        var diagnostic = Assert.Single(await Run("public static OrderLine Make() => new OrderLine();"));

        Assert.Contains("'OrderLine' is a child entity", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_keyed_by_several_columns_made_by_its_own_factory_is_fine()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new EntityCreationAnalyzer(), """
            using System.Collections.Generic;
            using Rask.Data;
            namespace Fuel;
            public sealed class DeliveryNote : Aggregate<int>
            {
                private readonly List<DeliveryNoteTransaction> _fuellings = [];
                public IReadOnlyList<DeliveryNoteTransaction> Fuellings => _fuellings;
                public void Fill(int transactionId) => _fuellings.Add(DeliveryNoteTransaction.For(transactionId, 1m));
            }
            public sealed class Transaction : Aggregate<int> { }
            public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
            {
                public int TransactionId { get; private set; }
                public decimal Quantity { get; private set; }
                internal static DeliveryNoteTransaction For(int transactionId, decimal quantity) =>
                    new() { TransactionId = transactionId, Quantity = quantity };
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_factory_the_holder_and_the_types_own_members_are_fine()
    {
        var diagnostics = await Run("public static Order Make() => Order.Place();");

        Assert.Empty(diagnostics);
    }
}
