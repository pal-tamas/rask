namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A generated model borrows what its aggregate states in code (FluentValidation): the model generator registers, for
/// each model, which of its members carries which of the aggregate's, and which lists hold rows. Rask's
/// FluentValidation integration is stubbed by name — the generator keys on <c>RaskValidators.Project</c> — so these
/// assert the registration's text; Rask.Data.Tests runs it.
/// </summary>
public class StatedRulesProjectionTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;

        namespace Rask { public static class RaskValidators { public static void Project() { } } }

        namespace Shop
        {
            public sealed class Supply : Aggregate<Guid>, IHasForm
            {
                private readonly List<SupplyPart> _parts = [];
                private Supply() { }
                public string Name { get; private set; } = "";
                public int Quantity { get; private set; }
                public decimal? Price { get; private set; }
                public IReadOnlyCollection<SupplyPart> Parts => _parts;
            }

            public sealed class SupplyPart : Entity<Guid>
            {
                private SupplyPart() { }
                public decimal Quantity { get; private set; }
            }
        }
        """;

    [Fact]
    public void Each_primitive_member_carries_the_aggregates_member_of_its_name()
    {
        var run = Run(Source);

        var model = run.GeneratedSource("Shop.SupplyModel");

        Assert.Contains("global::Rask.RaskValidators.Project<global::Shop.SupplyModel, global::Shop.Supply>(static __p => __p", model, StringComparison.Ordinal);
        Assert.Contains(".Member(static __m => __m.Name, \"Name\")", model, StringComparison.Ordinal);
        Assert.Contains(".Value(static __m => __m.Quantity, \"Quantity\")", model, StringComparison.Ordinal);
        Assert.Contains(".Member(static __m => __m.Price, \"Price\")", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_of_rows_borrows_the_rows_own_rules()
    {
        var run = Run(Source);

        var model = run.GeneratedSource("Shop.SupplyModel");
        var row = run.GeneratedSource("Shop.SupplyPartModel");

        Assert.Contains(".Rows(static __m => __m.Parts)", model, StringComparison.Ordinal);
        Assert.Contains("Project<global::Shop.SupplyPartModel, global::Shop.SupplyPart>", row, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_integration_nothing_is_registered()
    {
        var run = Run(Source.Replace("namespace Rask { public static class RaskValidators { public static void Project() { } } }", "", StringComparison.Ordinal));

        var model = run.GeneratedSource("Shop.SupplyModel");

        Assert.DoesNotContain("RaskValidators", model, StringComparison.Ordinal);
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(source, new ModelInputGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");
}
