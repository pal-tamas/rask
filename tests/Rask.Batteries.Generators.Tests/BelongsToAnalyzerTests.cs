using Microsoft.CodeAnalysis;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     RASK195: a BelongsTo loaded with the aggregate holding it. RASK196: another aggregate's id held by hand, where one
///     BelongsTo says the same.
/// </summary>
public class BelongsToAnalyzerTests
{
    // The generated half of `public partial BelongsTo<Vehicle>? Vehicle { get; }`, written out: no generator runs here.
    private const string Fleet = """
        using System;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;
        using Rask.Data;
        namespace Fleet;

        public readonly partial record struct VehicleId(int Value);

        public sealed class Vehicle : Aggregate<VehicleId>;

        public sealed class Driver : Aggregate<int>;

        public sealed class Depot : Aggregate<Guid>, ITenant;

        public sealed class Trip : Aggregate<Guid>
        {
            public HasMany<Leg> Legs { get; } = new();
        }

        public sealed class Leg : Entity<Guid>
        {
            public Guid TripId { get; private set; }
            public VehicleId VehicleId { get; private set; }
        }

        public sealed class Fueling : Aggregate<Guid>
        {
            public VehicleId? VehicleId { get; private set; }
            public BelongsTo<Vehicle>? Vehicle => VehicleId is { } id ? BelongsTo.Of<Vehicle, VehicleId>(id) : null;
        }
        """;

    [Theory]
    [InlineData("IQueryable<Fueling> q) => q.Include(f => f.Vehicle)", "Include")]
    [InlineData("IQueryable<Trip> q) => q.Include(t => t.Legs).ThenInclude(l => l.Vehicle)", "ThenInclude")]
    [InlineData("DbContext db, Fueling f) => db.Entry(f).Reference(x => x.Vehicle)", "Reference")]
    public async Task Loading_the_other_aggregate_with_the_one_that_refers_to_it_is_refused_where_it_is_written(string call, string method)
    {
        var source = Fleet.Replace("public HasMany<Leg> Legs", "public HasMany<LegWith> Legs", StringComparison.Ordinal) + $$"""
            public sealed class LegWith : Entity<Guid>
            {
                public VehicleId VehicleId { get; private set; }
                public BelongsTo<Vehicle> Vehicle => BelongsTo.Of<Vehicle, VehicleId>(VehicleId);
            }
            public static class Reads
            {
                public static object Load({{call}};
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new BelongsToLoadAnalyzer(), source);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("RASK195", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith($"'{method}' loads '", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("read it through its read face", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Including_a_child_list_or_reading_the_id_is_not_RASK195()
    {
        var source = Fleet + """
            public static class Reads
            {
                public static object Load(IQueryable<Trip> q) => q.Include(t => t.Legs);
                public static VehicleId? Id(Fueling f) => f.VehicleId;
            }
            """;

        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new BelongsToLoadAnalyzer(), source);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("public VehicleId? CarId { get; private set; }", "'Shift.CarId' holds the id of a 'Vehicle' by hand — declare 'public partial BelongsTo<Vehicle>? Car { get; }'")]
    [InlineData("public int DriverId { get; private set; }", "'Shift.DriverId' holds the id of a 'Driver' by hand — declare 'public partial BelongsTo<Driver> Driver { get; }'")]
    [InlineData("public int? RelieverDriverId { get; private set; }", "declare 'public partial BelongsTo<Driver>? RelieverDriver { get; }'")]
    [InlineData("public int DriverId { get; private set; } public Driver? Driver { get; private set; }", "declare 'public partial BelongsTo<Driver> Driver { get; }'")]
    public async Task Another_aggregates_id_held_by_hand_is_RASK196(string members, string message)
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new HeldReferenceAnalyzer(), Fleet + $$"""
            public sealed class Shift : Aggregate<Guid>
            {
                {{members}}
            }
            """);

        var diagnostic = Assert.Single(diagnostics, static d => d.Location.SourceSpan.Start > Fleet.Length);
        Assert.Equal("RASK196", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains(message, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_holding_another_aggregates_id_is_RASK196_but_not_its_way_back_to_its_root()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new HeldReferenceAnalyzer(), Fleet);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("VehicleId", diagnostic.Flagged());
        Assert.Contains("'Leg.VehicleId'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public Guid DepotId { get; private set; }")]
    [InlineData("public Guid ExternalId { get; private set; }")]
    [InlineData("public Guid DriverId { get; private set; }")]
    [InlineData("public int DriverId { get; private set; } public string Driver => \"\";")]
    public async Task The_tenant_an_id_naming_no_aggregate_another_key_type_and_a_taken_name_are_not_RASK196(string members)
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new HeldReferenceAnalyzer(), Fleet + $$"""
            public sealed class Shift : Aggregate<Guid>
            {
                {{members}}
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Location.SourceSpan.Start > Fleet.Length);
    }

    [Fact]
    public async Task An_entity_with_a_form_is_not_RASK196_as_its_form_does_not_carry_a_BelongsTo()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new HeldReferenceAnalyzer(), Fleet + """
            public sealed class Shift : Aggregate<Guid>, IHasForm
            {
                public int DriverId { get; private set; }
            }
            """);

        Assert.DoesNotContain(diagnostics, static d => d.Location.SourceSpan.Start > Fleet.Length);
    }

    [Fact]
    public async Task A_relationship_Configure_names_settles_the_aggregate_whatever_the_id_is_called()
    {
        var diagnostics = await ModelStateAnalyzerHarness.RunAsync(new HeldReferenceAnalyzer(), Fleet + """
            public sealed class Shift : Aggregate<Guid>
            {
                public int? LeadDriverId { get; private set; }
                public int? TransactionTypeId { get; private set; }

                public static void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Shift> b)
                {
                    b.HasOne<Driver>().WithMany().HasForeignKey(s => s.LeadDriverId);
                    b.HasOne<Driver>().WithMany().HasForeignKey(s => s.TransactionTypeId);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, static d => d.Location.SourceSpan.Start > Fleet.Length);
        Assert.Equal("LeadDriverId", diagnostic.Flagged());
    }
}
