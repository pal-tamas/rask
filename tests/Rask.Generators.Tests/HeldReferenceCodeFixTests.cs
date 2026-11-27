using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK196 quick-fix: another aggregate's id held by hand becomes one BelongsTo&lt;X&gt;.</summary>
public class HeldReferenceCodeFixTests
{
    private static string Source(string members, string shift = "public sealed class Shift : Aggregate<Guid>") => $$"""
        using System;
        using Rask.Data;
        namespace Fleet;
        public readonly partial record struct VehicleId(int Value);
        public sealed class Vehicle : Aggregate<VehicleId>;
        public sealed class Driver : Aggregate<int>;
        {{shift}}
        {
            {{members}}
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new HeldReferenceAnalyzer(), new HeldReferenceCodeFixProvider(), "RASK196", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    private static Task<bool> Offered(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new HeldReferenceAnalyzer(), new HeldReferenceCodeFixProvider(), "RASK196", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Fact]
    public async Task The_fix_turns_a_nullable_id_into_an_optional_BelongsTo_and_makes_the_class_partial()
    {
        var source = Source("""
            /// <summary>The car on the shift.</summary>
            public VehicleId? CarId { get; private set; }

            public void Take(VehicleId car) => CarId = car;
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("public sealed partial class Shift : Aggregate<Guid>", fixed_, StringComparison.Ordinal);
        Assert.Contains("/// <summary>The car on the shift.</summary>", fixed_, StringComparison.Ordinal);
        Assert.Contains("public partial BelongsTo<Vehicle>? Car { get; }", fixed_, StringComparison.Ordinal);
        Assert.Contains("public void Take(VehicleId car) => CarId = car;", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("CarId { get;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_turns_a_required_id_and_its_hand_written_navigation_into_one_required_BelongsTo()
    {
        var source = Source("""
            public int DriverId { get; private set; }
            public Driver? Driver { get; private set; }
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("public partial BelongsTo<Driver> Driver { get; }", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Driver? Driver", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("DriverId { get;", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[System.ComponentModel.DataAnnotations.Required] public int DriverId { get; private set; }")]
    [InlineData("public int DriverId { get; set; }")]
    [InlineData("public int DriverId { get; private set; } = 7;")]
    [InlineData("public int DriverId { get; private set; } public Driver? Driver { get; private set; } public string Name => Driver?.ToString() ?? \"\";")]
    public async Task The_fix_is_withheld_where_the_generated_id_would_not_be_the_one_written(string members)
    {
        var source = Source(members);

        var offered = await Offered(source);

        Assert.False(offered);
    }
}
