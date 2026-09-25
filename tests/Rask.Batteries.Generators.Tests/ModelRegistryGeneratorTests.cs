using System.Globalization;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// Drives <see cref="ModelRegistryGenerator"/> over hand-written entities.
/// </summary>
public class ModelRegistryGeneratorTests
{
    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source, new ModelRegistryGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void Two_entities_sharing_a_simple_name_in_two_namespaces_still_compile()
    {
        // The regression: the registry named its locals after the simple name, so both declared `__product`.
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop.Catalog { public sealed class Product : Aggregate<Guid> { } }
            namespace Shop.Archive { public sealed class Product : Aggregate<Guid> { } }
            """);

        // RASK090 at each declaration: both would be db.Products. The mapping is unaffected — only the
        // named set is refused.
        Assert.All(run.Diagnostics, d => Assert.Equal("RASK090", d.Id));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    // ---- named sets on DbContext --------------------------------------------------------------------

    [Theory]
    [InlineData("Order", "Orders")]
    [InlineData("OrderLine", "OrderLines")]
    [InlineData("Address", "Addresses")]
    [InlineData("Box", "Boxes")]
    [InlineData("Quiz", "Quizes")]   // the rule, not English — a doubled consonant is exactly the guess it refuses
    [InlineData("Batch", "Batches")]
    [InlineData("Dish", "Dishes")]
    [InlineData("Category", "Categories")]
    [InlineData("Day", "Days")]
    [InlineData("Person", "Persons")]
    public void A_set_is_named_by_the_documented_rule(string type, string set)
    {
        var run = Run($$"""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class {{type}} : Aggregate<Guid> { }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        var sets = run.GeneratedSource("__RaskDbSets.Shop");
        Assert.Contains($"> {set} => db.Set<global::Shop.{type}>();", sets, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_entity_gets_a_set_too_because_it_is_in_the_write_context()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Shop;
            public sealed class Order : Aggregate<Guid>
            {
                private readonly List<OrderLine> _lines = [];
                public IReadOnlyCollection<OrderLine> Lines => _lines;
            }
            public sealed class OrderLine : Entity<Guid> { }
            """);

        Assert.Empty(run.Diagnostics);

        // A child is not writable on its own, but it IS a table in this context — so Set<T>() reaches it, and
        // so does the named accessor. The border is about writes, not about what the context maps.
        var sets = run.GeneratedSource("__RaskDbSets.Shop");
        Assert.Contains("Orders => db.Set<global::Shop.Order>();", sets, StringComparison.Ordinal);
        Assert.Contains("OrderLines => db.Set<global::Shop.OrderLine>();", sets, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_entity_gets_an_internal_set()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            internal sealed class Ledger : Aggregate<Guid> { }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());

        // A public accessor returning DbSet<internal> would not compile, and would promise callers a type they
        // cannot name.
        Assert.Contains(
            "internal global::Microsoft.EntityFrameworkCore.DbSet<global::Shop.Ledger> Ledgers",
            run.GeneratedSource("__RaskDbSets.Shop"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_DbContext_already_declares_is_RASK090_and_generates_nothing()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class SaveChange : Aggregate<Guid> { }
            """);

        // db.SaveChanges would compile and silently mean DbContext.SaveChanges — a member on the type itself
        // always wins over an extension member — so the accessor is refused rather than emitted.
        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK090", diagnostic.Id);
        Assert.Contains("DbContext already declares 'SaveChanges'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Both_sides_of_a_set_name_clash_are_reported_where_they_are_declared()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop.Catalog { public sealed class Product : Aggregate<Guid> { } }
            namespace Shop.Archive { public sealed class Product : Aggregate<Guid> { } }
            """);

        // Reported at BOTH declarations: an author looking at either one has to see why db.Products is missing.
        Assert.Equal(2, run.Diagnostics.Count());
        Assert.All(run.Diagnostics, d => Assert.Equal("RASK090", d.Id));
        Assert.All(
            run.Diagnostics,
            d => Assert.Contains("'Product' want it too", d.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Fact]
    public void A_Configure_that_is_an_instance_method_is_RASK072()
    {
        var run = Run("""
            using System;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class Product : Aggregate<Guid>
            {
                public void Configure(EntityTypeBuilder<Product> builder) { }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK072", diagnostic.Id);
        Assert.Contains("is an instance method", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_strongly_typed_id_with_two_values_is_RASK073()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public readonly record struct PairId(Guid Left, Guid Right);
            public sealed class Pair : Aggregate<PairId> { }
            """);

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("RASK073", diagnostic.Id);
        Assert.Contains("2 public properties", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
