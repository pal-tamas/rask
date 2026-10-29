using System.Globalization;
using Microsoft.CodeAnalysis;

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

        public sealed class Customer : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
        {
            private Customer() { }
            public string Country { get; private set; } = "";
        }

        public sealed class User : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
        {
            private User() { }
            public string Name { get; private set; } = "";
        }

        public sealed record Money(decimal Amount, string Currency);

        public sealed class Order : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
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

        // The framework's own columns: Version because this is a root, and no DeletedAt because it did not ask
        // for soft delete.
        Assert.Contains("public global::System.Guid Id { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("public int Version { get; init; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletedAt", source, StringComparison.Ordinal);

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
    public void An_id_naming_another_aggregates_child_becomes_a_navigation_on_the_read_face()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Fuel;
            public sealed class Pump : Aggregate<Guid>
            {
                private readonly List<Pistol> _pistols = [];
                private Pump() { }
                public IReadOnlyList<Pistol> Pistols => _pistols;
            }
            public sealed class Pistol : Entity<Guid>
            {
                private Pistol() { }
                public int TankId { get; private set; }
            }
            public sealed class Fueling : Aggregate<Guid>
            {
                private Fueling() { }
                public Guid PistolId { get; private set; }
            }
            """);

        var face = run.GeneratedSource("Fuel_FuelingRead");

        Assert.Contains("public global::Fuel.PistolRead Pistol { get; init; }", face, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_id_naming_a_child_exactly_beats_an_aggregate_its_name_only_ends_in()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Fuel;
            public sealed class Tank : Aggregate<string>
            {
                private readonly List<VirtualTank> _virtualTanks = [];
                private Tank() { }
                public IReadOnlyList<VirtualTank> VirtualTanks => _virtualTanks;
            }
            public sealed class VirtualTank : Entity<string>
            {
                private VirtualTank() { }
                public string Name { get; private set; } = "";
            }
            public sealed class Supply : Aggregate<string>
            {
                private readonly List<SupplyPart> _parts = [];
                private Supply() { }
                public IReadOnlyList<SupplyPart> Parts => _parts;
            }
            public sealed class SupplyPart : Entity<string>
            {
                private SupplyPart() { }
                public string VirtualTankId { get; private set; } = "";
            }
            """);

        var face = run.GeneratedSource("Fuel_SupplyPartRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public global::Fuel.VirtualTankRead VirtualTank { get; init; }", face, StringComparison.Ordinal);
        Assert.DoesNotContain("TankRead VirtualTank", face.Replace("VirtualTankRead", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
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
    public void Queries_open_off_the_entity_itself()
    {
        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        Assert.Contains("extension(global::Shop.Order)", source, StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Rask.Data.ModelQuery<global::Shop.OrderRead> Where(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(" Read =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_opening_operator_of_ModelQuery_is_on_the_entity()
    {
        // ThenBy continues an order, so it only exists once a query does; awaiting runs a query, so the entity
        // opens one with All rather than being awaitable itself.
        var operators = typeof(Rask.Data.ModelQuery<>)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(name => !name.StartsWith("ThenBy", StringComparison.Ordinal))
            .Where(name => name is not ("GetAwaiter" or "ConfigureAwait"))
            .Distinct();

        var source = Run(Shop).GeneratedSource("Shop_OrderRead");

        Assert.All(operators, name => Assert.Matches($@"public static [^\n]* {name}(<\w+>)?\(", source));
        Assert.Contains("public static global::Rask.Data.ModelQuery<global::Shop.OrderRead> All =>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_naming_an_aggregate_with_the_wrong_key_type_is_reported()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Customer : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
            {
                private Customer() { }
            }
            public sealed class Order : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
            {
                private Order() { }
                public int CustomerId { get; private set; }
            }
            """);

        var diagnostic = Assert.Single(run.Diagnostics, d => d.Id == "RASK089");
        Assert.Contains("not that entity's key type", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CustomerRead Customer", run.GeneratedSource("Shop_OrderRead"), StringComparison.Ordinal);
    }

    private const string Distributions = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Fuel;
        public sealed class Tank : Aggregate<string>
        {
            private readonly List<VirtualTank> _virtualTanks = [];
            private Tank() { }
            public IReadOnlyList<VirtualTank> VirtualTanks => _virtualTanks;
        }
        public sealed class VirtualTank : Entity<string>
        {
            private VirtualTank() { }
        }
        public sealed class Fueling : Aggregate<string>
        {
            private Fueling() { }
            public decimal Quantity { get; private set; }
        }
        public sealed class Distribution : Aggregate<string>
        {
            private Distribution() { }
            public string FromFuelingId { get; private set; } = "";
            public string ToFuelingId { get; private set; } = "";
            public string? FuelingId { get; private set; }
            public string VirtualTankId { get; private set; } = "";
            public decimal Quantity { get; private set; }
        }
        """;

    [Fact]
    public void Every_inferred_navigation_gives_its_target_the_rows_that_point_there_named_by_the_ids_prefix()
    {
        var run = Run(Distributions);

        var fueling = run.GeneratedSource("Fuel_FuelingRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("IReadOnlyList<global::Fuel.DistributionRead> DistributionsFrom { get; init; }", fueling, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Fuel.DistributionRead> DistributionsTo { get; init; }", fueling, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<global::Fuel.DistributionRead> Distributions { get; init; }", fueling, StringComparison.Ordinal);
        Assert.Contains("\"FromFueling\", typeof(global::Fuel.FuelingRead), \"FromFuelingId\", \"DistributionsFrom\")", registry, StringComparison.Ordinal);
        Assert.Contains("\"Fueling\", typeof(global::Fuel.FuelingRead), \"FuelingId\", \"Distributions\")", registry, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_child_an_id_points_at_gets_the_rows_that_point_there_too()
    {
        var run = Run(Distributions);

        var face = run.GeneratedSource("Fuel_VirtualTankRead");

        Assert.Contains("IReadOnlyList<global::Fuel.DistributionRead> Distributions { get; init; }", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reverse_name_the_target_already_uses_is_left_out_and_its_navigation_kept()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Fuel;
            public sealed class Fueling : Aggregate<string>
            {
                private Fueling() { }
                public int DistributionsFrom { get; private set; }
            }
            public sealed class Distribution : Aggregate<string>
            {
                private Distribution() { }
                public string FromFuelingId { get; private set; } = "";
                public string ToFuelingId { get; private set; } = "";
            }
            """);

        var fueling = run.GeneratedSource("Fuel_FuelingRead");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Contains("public int DistributionsFrom { get; init; }", fueling, StringComparison.Ordinal);
        Assert.Contains("DistributionRead> DistributionsTo { get; init; }", fueling, StringComparison.Ordinal);
        Assert.Contains("\"FromFueling\", typeof(global::Fuel.FuelingRead), \"FromFuelingId\"),", registry, StringComparison.Ordinal);
        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal(("RASK108", DiagnosticSeverity.Warning), (diagnostic.Id, diagnostic.Severity));
        Assert.Equal(
            "FuelingRead does not get 'DistributionsFrom': the name is already a column of Fueling. Rename the id (Distribution.FromFuelingId) or the column.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_reverse_name_that_is_already_a_navigation_of_the_target_is_reported_and_left_out()
    {
        var run = Run("""
            using Rask.Data;
            namespace Fuel;
            public sealed class Lines : Aggregate<string>
            {
                private Lines() { }
            }
            public sealed class Fueling : Aggregate<string>
            {
                private Fueling() { }
                public string LinesId { get; private set; } = "";
            }
            public sealed class Line : Aggregate<string>
            {
                private Line() { }
                public string FuelingId { get; private set; } = "";
            }
            """);

        var fueling = run.GeneratedSource("Fuel_FuelingRead");

        Assert.Contains("public global::Fuel.LinesRead Lines { get; init; }", fueling, StringComparison.Ordinal);
        Assert.DoesNotContain("IReadOnlyList<global::Fuel.LineRead>", fueling, StringComparison.Ordinal);
        Assert.Equal(
            "FuelingRead does not get 'Lines': the name is already a navigation of FuelingRead. Rename the id (Line.FuelingId) or the navigation's id.",
            Assert.Single(run.Diagnostics, d => d.Id == "RASK108").GetMessage(CultureInfo.InvariantCulture));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Two_ids_that_would_give_one_target_the_same_collection_are_both_reported_and_left_out()
    {
        var run = Run("""
            using Rask.Data;
            namespace Fuel
            {
                public sealed class Fueling : Aggregate<string>
                {
                    private Fueling() { }
                }
            }
            namespace Fuel.Old
            {
                public sealed class Distribution : Aggregate<string>
                {
                    private Distribution() { }
                    public string FuelingId { get; private set; } = "";
                }
            }
            namespace Fuel.New
            {
                public sealed class Distribution : Aggregate<string>
                {
                    private Distribution() { }
                    public string FuelingId { get; private set; } = "";
                }
            }
            """);

        var fueling = run.GeneratedSource("Fuel_FuelingRead");
        var messages = run.Diagnostics.Where(d => d.Id == "RASK108").Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();

        Assert.DoesNotContain("Distributions", fueling, StringComparison.Ordinal);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Contains(
            "the name is already the collection for Distribution.FuelingId. Rename the id (Distribution.FuelingId) or the other id.", m, StringComparison.Ordinal));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_self_reference_stays_a_column()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Category : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
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

    [Fact]
    public void An_id_that_ends_in_its_own_aggregates_name_is_a_join_to_another_row_of_the_table()
    {
        var run = Run("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Supply : Aggregate<int>, ITimestamps, IVersioned, IFormModel
            {
                private Supply() { }
                public int? SumSupplyId { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop_SupplyRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public int? SumSupplyId { get; init; }", source, StringComparison.Ordinal);
        Assert.Contains("SupplyRead? SumSupply { get; init; }", source, StringComparison.Ordinal);
    }

    // Three ways an entity can stand to the tenant column: partitioned and silent (a shadow column),
    // partitioned and reading it, and not partitioned at all but keeping a tenant as data of its own.
    private const string Tenants = """
        using System;
        using Rask.Data;
        namespace Shop;

        public sealed class Invoice : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IPerTenant
        {
            private Invoice() { }
            public string Number { get; private set; } = "";
        }

        public sealed class Receipt : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel, IPerTenant
        {
            private Receipt() { }
            public Guid? TenantId { get; private set; }
            public string Number { get; private set; } = "";
        }

        public sealed class Delivery : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
        {
            private Delivery() { }
            public Guid? TenantId { get; set; }
            public string Number { get; private set; } = "";
        }

        public sealed class Tank : Aggregate<int>, IPerTenant<int>
        {
            private readonly System.Collections.Generic.List<Compartment> _compartments = new();
            private Tank() { }
            public string Number { get; private set; } = "";
            public System.Collections.Generic.IReadOnlyList<Compartment> Compartments => _compartments;
        }

        public sealed class Compartment : Entity<int>
        {
            private Compartment() { }
            public string Number { get; private set; } = "";
        }

        public sealed class Crate : Aggregate<long>, IPerTenant
        {
            private Crate() { }
            public long? TenantId { get; private set; }
            public string Number { get; private set; } = "";
        }
        """;

    [Theory]
    [InlineData("Invoice", "global::System.Guid")]
    [InlineData("Receipt", "global::System.Guid")]
    [InlineData("Tank", "int")]
    [InlineData("Crate", "long")]
    public void A_per_tenant_root_reads_its_tenant_in_the_columns_own_type_declared_or_not(string entity, string type)
    {
        var run = Run(Tenants);

        var source = run.GeneratedSource($"Shop_{entity}Read");
        var registry = run.GeneratedSource("__RaskReadModelRegistry");

        Assert.Empty(run.Diagnostics);
        Assert.Contains($"public {type}? TenantId {{ get; init; }}", source, StringComparison.Ordinal);
        Assert.Contains("new global::Rask.Data.ReadColumnMapping(\"TenantId\", \"TenantId\", \"TenantId\")", registry, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Delivery")]
    [InlineData("Compartment")]
    public void A_table_that_is_not_a_per_tenant_root_keeps_its_tenant_off_its_read_face(string entity)
    {
        var run = Run(Tenants);

        var source = run.GeneratedSource($"Shop_{entity}Read");

        Assert.Empty(run.Diagnostics);
        Assert.Contains("public string Number { get; init; } = null!;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tenant", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_view_computed_from_a_stored_list_is_not_on_the_read_face_and_the_list_is()
    {
        var run = Run("""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Data;
            namespace Shop;
            public sealed class Basket : Aggregate<Guid>
            {
                private readonly List<BasketLine> _lines = new();
                private readonly List<string> _tags = new();
                private Basket() { }
                public IReadOnlyList<BasketLine> Lines => _lines;
                public IEnumerable<BasketLine> OpenLines => _lines.Where(l => l.Quantity > 0);
                public IReadOnlyList<string> Tags => _tags;
                public IReadOnlyList<string> SortedTags => _tags.OrderBy(t => t).ToList();
            }
            public sealed class BasketLine : Entity<Guid>
            {
                private BasketLine() { }
                public int Quantity { get; private set; }
            }
            """);

        var source = run.GeneratedSource("Shop_BasketRead");

        Assert.Empty(run.Diagnostics);
        Assert.Contains(" Lines { get; init; } = [];", source, StringComparison.Ordinal);
        Assert.Contains(" Tags { get; init; } = [];", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenLines", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SortedTags", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tenant_on_a_read_face_is_a_column_and_never_a_navigation_to_an_aggregate_called_Tenant()
    {
        var run = Run(Tenants + """

            public sealed class Tenant : Aggregate<Guid>
            {
                private Tenant() { }
                public string Name { get; private set; } = "";
            }
            """);

        var source = run.GeneratedSource("Shop_InvoiceRead");

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::System.Guid? TenantId { get; init; }", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantRead", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "CreatedAt")]
    [InlineData("", "UpdatedAt")]
    [InlineData("ICreated", "UpdatedAt")]
    [InlineData("IUpdated", "CreatedAt")]
    [InlineData("ITimestamps", "Version")]
    [InlineData("ITimestamps, IVersioned", "DeletedAt")]
    [InlineData("ITimestamps, IVersioned, IFormModel", "DeletedAt")]
    public void A_column_no_trait_asks_for_is_not_on_the_read_face(string traits, string column)
    {
        var run = Run($$"""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Depot : {{DepotBase(traits)}}
            {
                private Depot() { }
                public string Name { get; private set; } = "";
            }
            """);

        var source = run.GeneratedSource("Shop_DepotRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(" " + column + " {", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ITimestamps", "CreatedAt")]
    [InlineData("ITimestamps", "UpdatedAt")]
    [InlineData("IVersioned", "Version")]
    [InlineData("ICreated", "CreatedAt")]
    [InlineData("ISoftDeletable", "DeletedAt")]
    public void A_column_a_trait_asks_for_stays_on_the_read_face(string traits, string column)
    {
        var run = Run($$"""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class Depot : {{DepotBase(traits)}}
            {
                private Depot() { }
                public string Name { get; private set; } = "";
            }
            """);

        var source = run.GeneratedSource("Shop_DepotRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" " + column + " {", source, StringComparison.Ordinal);
    }

    private static string DepotBase(string traits) =>
        traits.Length == 0 ? "Aggregate<int>" : "Aggregate<int>, " + traits;
}
