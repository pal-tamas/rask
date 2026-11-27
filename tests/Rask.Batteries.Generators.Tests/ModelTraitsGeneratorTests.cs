namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// The traits that carry a type or reach a child — a child's <c>ISoftDeletes</c>, <c>IBelongsToTenantThrough&lt;T&gt;</c>
/// — as the registry records them and the read faces show them, and <c>Across(reason)</c> on every face.
/// </summary>
public class ModelTraitsGeneratorTests
{
    private const string Registry = "global::Rask.Data.ConventionRegistry.";

    [Fact]
    public void A_soft_removed_child_is_registered_with_the_trait_and_reads_its_DeletedAt()
    {
        var run = Run();

        var stall = run.GeneratedSource("Yard_StallRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" DeletedAt {", stall, StringComparison.Ordinal);
        Assert.Contains(Registry + "Declare(typeof(global::Yard.Stall), (global::Rask.Data.TraitSet)640);", Conventions(run), StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_through_another_is_partitioned_names_its_parent_and_reads_no_tenant_column()
    {
        var run = Run();

        var ticket = run.GeneratedSource("Yard_TicketRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("TenantId", ticket, StringComparison.Ordinal);
        Assert.Contains(Registry + "Declare(typeof(global::Yard.Ticket), (global::Rask.Data.TraitSet)128);", Conventions(run), StringComparison.Ordinal);
        Assert.Contains(Registry + "DeclareTenantParent(typeof(global::Yard.Ticket), typeof(global::Yard.Port));", Conventions(run), StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_keeps_one_child_in_force_says_nothing_of_it_the_child_says_when()
    {
        var run = Run();

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("InForce", Conventions(run), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_read_face_opens_a_query_across_tenants_with_a_reason()
    {
        var run = Run();

        var port = run.GeneratedSource("Yard_PortRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Across(string reason)", port, StringComparison.Ordinal);
    }

    private static string Conventions(GeneratorRun run) => run.GeneratedSource("__RaskConventions");

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(
            """
            using System;
            using System.Collections.Generic;
            using Rask.Data;
            namespace Yard;

            public sealed class Port : Aggregate<Guid>, IBelongsTo<GuidTenant>
            {
                private readonly List<Stall> _stalls = [];
                private readonly List<Filing> _filings = [];
                private Port() { }
                public string Name { get; private set; } = "";
                public IReadOnlyList<Stall> Stalls => _stalls;
                public IReadOnlyList<Filing> Filings => _filings;
            }

            public sealed class Stall : Entity<Guid>, ISoftDeletes
            {
                private Stall() { }
                public string Name { get; private set; } = "";
            }

            public sealed class Filing : Entity<Guid>
            {
                private Filing() { }
                public DateTime? UnfiledAt { get; private set; }
            }

            public sealed class Ticket : Aggregate<Guid>, IBelongsToTenantThrough<Port>
            {
                private Ticket() { }
                public Guid PortId { get; private set; }
                public string Note { get; private set; } = "";
            }
            public sealed class GuidTenant : Rask.Data.Aggregate<System.Guid> { }
            """,
            [new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");
}
