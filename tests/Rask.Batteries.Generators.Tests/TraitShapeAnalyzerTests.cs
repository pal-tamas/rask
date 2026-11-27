using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>RASK146: a trait the class cannot carry — where the base's type arguments had the compiler refuse it.</summary>
public class TraitShapeAnalyzerTests
{
    [Theory]
    [InlineData("IVersioned")]
    [InlineData("IHasForm")]
    [InlineData("IDeletable")]
    [InlineData("IBroadcasts")]
    [InlineData("IBelongsTo<Shop>")]
    public async Task An_aggregates_trait_on_a_child_entity_is_RASK146(string trait)
    {
        var source = $$"""
            using Rask.Data;
            namespace Yard;
            public sealed class Shop : Aggregate<int> { }
            public sealed class Line : Entity<int>, {{trait}} { }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new TraitShapeAnalyzer(), source);

        var error = Assert.Single(diagnostics, d => d.Id == "RASK146");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("'Line' says", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("only an aggregate root carries that", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_says_its_timestamps_and_its_soft_delete_with_nothing_reported()
    {
        const string source = """
            using Rask.Data;
            namespace Yard;
            public sealed class Line : Entity<int>, IHasTimestamps, ISoftDeletes, IInfrastructure { }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new TraitShapeAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RASK146");
    }

    [Theory]
    [InlineData("IBelongsTo<Shop>, IBelongsToTenantThrough<Port>", "IBelongsToTenantThrough as well")]
    [InlineData("IDeletable, ISoftDeletes", "drop IDeletable")]
    public async Task Two_traits_that_contradict_each_other_are_RASK146(string traits, string fix)
    {
        var source = $$"""
            using Rask.Data;
            namespace Yard;
            public sealed class Shop : Aggregate<int> { }
            public sealed class Port : Aggregate<int>, IBelongsTo<Shop> { }
            public sealed class Ticket : Aggregate<int>, {{traits}} { }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new TraitShapeAnalyzer(), source);

        var error = Assert.Single(diagnostics, d => d.Id == "RASK146");
        Assert.Contains(fix, error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_table_belonging_to_an_aggregate_other_than_the_apps_tenant_is_RASK146_naming_it()
    {
        const string source = """
            using Rask.Data;
            namespace Yard;
            public sealed class Tenant : Aggregate<int>, ITenant { }
            public sealed class Branch : Aggregate<int> { }
            public sealed class Invoice : Aggregate<int>, IBelongsTo<Branch> { }
            public sealed class Receipt : Aggregate<int>, IBelongsTo<Tenant> { }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new TraitShapeAnalyzer(), source);

        var error = Assert.Single(diagnostics, d => d.Id == "RASK146");
        Assert.Contains("'Invoice' says IBelongsTo<Branch>", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("say IBelongsTo<Tenant>", error.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tables_of_several_tenants_with_no_app_tenant_are_a_test_assemblys_and_say_nothing()
    {
        const string source = """
            using Rask.Data;
            namespace Yard;
            public sealed class GuidShop : Aggregate<System.Guid> { }
            public sealed class NumberShop : Aggregate<int> { }
            public sealed class Invoice : Aggregate<int>, IBelongsTo<GuidShop> { }
            public sealed class Receipt : Aggregate<int>, IBelongsTo<NumberShop> { }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new TraitShapeAnalyzer(), source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "RASK146");
    }
}
