namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// Drives <see cref="ReadModelGenerator"/> over hand-written entities.
/// </summary>
/// <remarks>
/// What the read face IS — primitives, flattened value objects, inferred navigations — is decided here, from
/// symbols alone. Where it LANDS is mirrored from the built EF model at runtime, so the column names asserted
/// here are the convention's answer and <c>Rask.Data.Tests</c> is what proves both halves agree.
/// </remarks>
public class ReadModelGeneratorTests
{
    private const string Shop = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Shop;

        public sealed class Customer : Aggregate<Guid>
        {
            private Customer() { }
            public string Country { get; private set; } = "";
        }

        public sealed class User : Aggregate<Guid>
        {
            private User() { }
            public string Name { get; private set; } = "";
        }

        public sealed record Money(decimal Amount, string Currency);

        public sealed class Order : Aggregate<Guid>
        {
            private readonly List<OrderLine> _lines = [];
            private Order() { }
            public Guid CustomerId { get; private set; }
            public Guid? ShippedByUserId { get; private set; }
            public Guid ExternalRef { get; private set; }
            public string Reference { get; private set; } = "";
            public Money Total { get; private set; } = new(0m, "EUR");
            public IReadOnlyCollection<OrderLine> Lines => _lines;
            public bool IsEmpty => _lines.Count == 0;
        }

        public sealed class OrderLine : Entity<Guid>
        {
            private OrderLine() { }
            public int Quantity { get; private set; }
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ReadModelGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_read_face_is_primitives_and_carries_no_behaviour()
    {
        var run = Run(Shop);
        var source = run.GeneratedSource("Shop_OrderRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("sealed class OrderRead : global::Rask.Data.IReadModel", source, StringComparison.Ordinal);
        Assert.Contains("public string Reference { get; init; } = null!;", source, StringComparison.Ordinal);

        // The framework's own columns, and Version/DeletedAt only because this is a root.
        Assert.Contains("public global::System.Guid Id { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public int Version { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public global::System.DateTime? DeletedAt { get; init; }", source, StringComparison.Ordinal);

        // Computed — no column on the write side, so no member here either.
        Assert.DoesNotContain("IsEmpty", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_is_flattened_to_the_columns_it_maps_to()
    {
        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        Assert.Contains("public decimal TotalAmount { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public string TotalCurrency { get; init; } = null!;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Money Total", source, StringComparison.Ordinal);

        // The column, not the member name: Total_Amount is what the write model already owns.
        var registry = Run(Shop).GeneratedSource("__RaskReadModelRegistry");
        Assert.Contains("\"TotalAmount\", \"Total.Amount\", \"Total_Amount\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_whose_name_and_key_match_an_aggregate_becomes_a_navigation()
    {
        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        // Exact match, and a suffix match that keeps the PROPERTY's name so two references cannot collide.
        Assert.Contains("public global::Shop.CustomerRead Customer { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.UserRead? ShippedByUser { get; init; }", source, StringComparison.Ordinal);

        // The ids stay as columns beside them.
        Assert.Contains("public global::System.Guid CustomerId { get; init; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_matching_no_aggregate_stays_an_ordinary_column()
    {
        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        Assert.Contains("public global::System.Guid ExternalRef { get; init; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalRead", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_gets_its_own_face_and_a_navigation_back_to_its_root()
    {
        var run = Run(Shop);

        Assert.Contains(
            "public global::System.Collections.Generic.List<global::Shop.OrderLineRead> Lines { get; init; } = [];",
            run.GeneratedSource("Shop_OrderRead"),
            StringComparison.Ordinal);

        var child = run.GeneratedSource("Shop_OrderLineRead");
        Assert.Contains("public global::Shop.OrderRead Order { get; init; } = null!;", child, StringComparison.Ordinal);

        // A child is not a root: no version, no soft delete.
        Assert.DoesNotContain("Version", child, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedAt", child, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_is_put_on_the_entity_itself()
    {
        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        Assert.Contains("extension(global::Shop.Order)", source, StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Rask.Data.ModelQuery<global::Shop.OrderRead> Read =>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_naming_an_aggregate_with_the_wrong_key_type_is_reported()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Customer : Aggregate<Guid>
            {
                private Customer() { }
            }
            public sealed class Order : Aggregate<Guid>
            {
                private Order() { }
                public int CustomerId { get; private set; }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK089");
        Assert.Contains("not that aggregate's key type", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CustomerRead Customer", run.GeneratedSource("Shop_OrderRead"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_self_reference_stays_a_column()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Category : Aggregate<Guid>
            {
                private Category() { }
                public Guid? ParentId { get; private set; }
                public Guid? CategoryId { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop_CategoryRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::System.Guid? CategoryId { get; init; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CategoryRead Category {", source, StringComparison.Ordinal);
    }
}
