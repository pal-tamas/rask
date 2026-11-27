using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     <c>public partial BelongsTo&lt;Vehicle&gt;? Vehicle { get; }</c>: the generated foreign key and reference, the read
///     face's column and navigation, the reads a reference gets, and the declarations that cannot be completed (RASK194).
/// </summary>
public class BelongsToGeneratorTests
{
    private const string Fleet = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Fleet;

        public readonly partial record struct VehicleId(int Value);

        public sealed class Owner : Aggregate<Guid>
        {
            public string Name { get; private set; } = "";
        }

        public sealed class Vehicle : Aggregate<VehicleId>
        {
            public string Plate { get; private set; } = "";
            public Guid OwnerId { get; private set; }
        }

        public sealed class Driver : Aggregate<int>
        {
            public string Name { get; private set; } = "";
        }
        """;

    private const string Fueling = Fleet + """
        public sealed partial class Fueling : Aggregate<Guid>
        {
            public decimal Litres { get; private set; }

            public partial BelongsTo<Vehicle>? Vehicle { get; }

            public partial BelongsTo<Driver> FilledBy { get; }

            public void Fill(VehicleId vehicle, int driver)
            {
                VehicleId = vehicle;
                FilledById = driver;
            }
        }
        """;

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new BelongsToGenerator(), new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    private static IEnumerable<string> Rask194(GeneratorRun run) =>
        run.Diagnostics.Where(static d => d.Id == "RASK194").Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void An_optional_reference_generates_a_nullable_id_of_the_aggregates_key_type()
    {
        var run = Run(Fueling);

        var generated = run.GeneratedSource("Fleet.Fueling.BelongsTo");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask194(run));
        Assert.Contains("public global::Fleet.VehicleId? VehicleId { get; private set; }", generated, StringComparison.Ordinal);
        Assert.Contains("[global::System.ComponentModel.DataAnnotations.Schema.NotMapped]", generated, StringComparison.Ordinal);
        Assert.Contains(
            "public partial global::Rask.Data.BelongsTo<global::Fleet.Vehicle>? Vehicle =>\n        VehicleId is { } id ? global::Rask.Data.BelongsTo.Of<global::Fleet.Vehicle, global::Fleet.VehicleId>(id) : null;",
            generated.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_reference_generates_a_required_id_named_after_the_property()
    {
        var generated = Run(Fueling).GeneratedSource("Fleet.Fueling.BelongsTo");

        Assert.Contains("public int FilledById { get; private set; }", generated, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Data.BelongsTo.Of<global::Fleet.Driver, int>(FilledById);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_reference_is_registered_as_its_relationship_when_the_assembly_loads()
    {
        var generated = Run(Fueling).GeneratedSource("Fleet.Fueling.BelongsTo");

        Assert.Contains("[global::System.Runtime.CompilerServices.ModuleInitializer]", generated, StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Data.ConventionRegistry.DeclareBelongsTo<global::Fleet.Fueling, global::Fleet.Vehicle>(\"VehicleId\");",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Data.ConventionRegistry.DeclareBelongsTo<global::Fleet.Fueling, global::Fleet.Driver>(\"FilledById\");",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_read_face_has_the_id_and_a_navigation_named_by_the_declaration_with_no_RASK089()
    {
        var run = Run(Fueling);

        var face = run.GeneratedSource("Fleet_FuelingRead");

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id is "RASK089" or "RASK112");
        Assert.Contains("public global::Fleet.VehicleId? VehicleId { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public global::Fleet.VehicleRead? Vehicle { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public int FilledById { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public global::Fleet.DriverRead FilledBy { get; init; } = null!;", face, StringComparison.Ordinal);
    }

    [Fact]
    public void The_aggregate_referred_to_reads_back_the_rows_that_point_at_it()
    {
        var face = Run(Fueling).GeneratedSource("Fleet_VehicleRead");

        Assert.Contains("global::System.Collections.Generic.IReadOnlyList<global::Fleet.FuelingRead> Fuelings", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reference_reads_its_id_and_its_row_one_at_a_time_or_all_at_once()
    {
        var face = Run(Fueling).GeneratedSource("Fleet_VehicleRead");

        Assert.Contains("extension(global::Rask.Data.BelongsTo<global::Fleet.Vehicle> reference)", face, StringComparison.Ordinal);
        Assert.Contains("public global::Fleet.VehicleId Id => (global::Fleet.VehicleId)reference.Key;", face, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.Task<global::Fleet.VehicleRead?> Read(", face, StringComparison.Ordinal);
        Assert.Contains("public global::System.Threading.Tasks.Task<TResult?> Read<TResult>(", face, StringComparison.Ordinal);
        Assert.Contains(
            "extension(global::System.Collections.Generic.IEnumerable<global::Rask.Data.BelongsTo<global::Fleet.Vehicle>?> references)",
            face,
            StringComparison.Ordinal);
        Assert.Contains(
            "IReadOnlyDictionary<global::Fleet.VehicleId, global::Fleet.VehicleRead>> Read(",
            face,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_nothing_refers_to_gets_no_reference_reads()
    {
        var face = Run(Fueling).GeneratedSource("Fleet_OwnerRead");

        Assert.DoesNotContain("BelongsTo", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relationship_Configure_also_writes_out_is_the_one_navigation_named_by_the_declaration()
    {
        var run = Run(Fleet + """
            public sealed partial class Fueling : Aggregate<Guid>
            {
                public partial BelongsTo<Vehicle>? Vehicle { get; }

                public static void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Fueling> b) =>
                    b.HasOne<Vehicle>().WithMany().HasForeignKey(f => f.VehicleId).OnDelete(Microsoft.EntityFrameworkCore.DeleteBehavior.Restrict);
            }
            """);

        var face = run.GeneratedSource("Fleet_FuelingRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id is "RASK089" or "RASK112");
        Assert.Equal(2, face.Split("VehicleRead? Vehicle { get; init; }").Length);
    }

    [Fact]
    public void A_reference_on_a_child_names_another_aggregate_like_one_on_its_root()
    {
        var run = Run(Fleet + """
            public sealed class Trip : Aggregate<Guid>
            {
                public HasMany<Leg> Legs { get; } = new();
            }

            public sealed partial class Leg : Entity<Guid>
            {
                public partial BelongsTo<Vehicle> Vehicle { get; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask194(run));
        Assert.Contains("public global::Fleet.VehicleId VehicleId { get; private set; }", run.GeneratedSource("Fleet.Leg.BelongsTo"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public BelongsTo<Vehicle>? Vehicle { get; }", "it is not declared 'partial BelongsTo<Vehicle>? Vehicle { get; }'")]
    [InlineData("public partial BelongsTo<Vehicle>? Vehicle { get; set; }", "it is not declared 'partial BelongsTo<Vehicle>? Vehicle { get; }'")]
    [InlineData("public partial BelongsTo<Vehicle>? Vehicle { get; } public int? VehicleId { get; private set; }", "'Fueling' already has a 'VehicleId'")]
    public void A_declaration_Rask_cannot_complete_is_RASK194(string members, string reason)
    {
        var run = Run(Fleet + $$"""
            public sealed partial class Fueling : Aggregate<Guid>
            {
                {{members}}
            }
            """);

        var message = Assert.Single(Rask194(run));
        Assert.StartsWith("'Fueling.Vehicle' cannot be a BelongsTo: ", message, StringComparison.Ordinal);
        Assert.Contains(reason, message, StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("Fleet.Fueling.BelongsTo"));
    }

    [Theory]
    [InlineData("public sealed class Fueling : Aggregate<Guid>", "BelongsTo<Vehicle>", "'Fueling' is not partial")]
    [InlineData("public sealed partial class Fueling", "BelongsTo<Vehicle>", "'Fueling' is no entity")]
    [InlineData("public sealed partial class Fueling : Aggregate<Guid>, IHasForm", "BelongsTo<Vehicle>", "'Fueling' has a form, which does not carry a BelongsTo yet")]
    [InlineData("public sealed partial class Fueling : Aggregate<Guid>", "BelongsTo<Depot>", "'Depot' is the tenant, which IBelongsTo<Depot> keeps")]
    [InlineData("public sealed partial class Fueling : Aggregate<Guid>", "BelongsTo<Nozzle>", "'Nozzle' is a child of another aggregate")]
    public void A_reference_its_type_or_target_cannot_carry_is_RASK194(string declaration, string type, string reason)
    {
        var run = Run(Fleet + $$"""
            public sealed class Depot : Aggregate<Guid>, ITenant;
            public sealed class Pump : Aggregate<Guid>
            {
                public HasMany<Nozzle> Nozzles { get; } = new();
            }
            public sealed class Nozzle : Entity<Guid>;
            {{declaration}}
            {
                public partial {{type}}? Vehicle { get; }
            }
            """);

        Assert.Contains(Rask194(run), message => message.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_child_referring_to_the_root_that_holds_it_is_RASK194()
    {
        var run = Run(Fleet + """
            public sealed class Trip : Aggregate<Guid>
            {
                public HasMany<Leg> Legs { get; } = new();
            }

            public sealed partial class Leg : Entity<Guid>
            {
                public partial BelongsTo<Trip> Trip { get; }
            }
            """);

        Assert.Contains(Rask194(run), static m => m.Contains("'Leg' is a child of 'Trip', which reaches it already", StringComparison.Ordinal));
    }

    [Fact]
    public void The_RASK194_descriptor_is_an_error_with_its_help_link()
    {
        var descriptor = BelongsToGenerator.Rask194;

        Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
        Assert.EndsWith("#rask194", descriptor.HelpLinkUri, StringComparison.Ordinal);
    }
}
