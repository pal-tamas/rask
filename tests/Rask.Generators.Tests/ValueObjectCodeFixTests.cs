using Rask.Batteries.Generators;
using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK127 and RASK129 quick-fixes, applied and compared.</summary>
public class ValueObjectCodeFixTests
{
    // The generated half's Create, written by hand: the analyzer runs without the generator.
    private static string Source(string elsewhere) => $$"""
        using Rask.Data;
        namespace Shop;
        public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10)
        {
            public static Plate Create(string Value, string? field = null) => new(Value);
        }
        public sealed partial record Km(int Start, int End) : Rask.Data.Range(Start, End)
        {
            public static Km Create(int Start, int End, string? field = null) => new(Start, End);
        }
        public sealed class Trip
        {
            public Km Leg { get; } = Km.Create(1, 2);
        }
        public static class Elsewhere
        {
            {{elsewhere}}
        }
        """;

    private static Task<string> Fix127(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ValueObjectCreationAnalyzer(), new ValueObjectCreationCodeFixProvider(), "RASK127", source, "Rask.Data", "Rask.Cqrs");

    private static Task<bool> Offered127(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ValueObjectCreationAnalyzer(), new ValueObjectCreationCodeFixProvider(), "RASK127", source, "Rask.Data", "Rask.Cqrs");

    private static Task<string> Fix129(string declaration) =>
        CodeFixHarness.ApplyGeneratorFixAsync(
            new ValueObjectFactoryGenerator(), new ValueObjectModifiersCodeFixProvider(), "RASK129",
            "using Rask.Data;\nnamespace Shop;\n" + declaration + "\n", "Rask.Data", "Rask.Cqrs");

    [Theory]
    [InlineData("public static Plate Make(string v) => new Plate(v);", "public static Plate Make(string v) => Plate.Create(v);")]
    [InlineData("public static Plate Make(string v) => new(v);", "public static Plate Make(string v) => Plate.Create(v);")]
    [InlineData("public static Km Make(int a) => new Km(a, a + 1);", "public static Km Make(int a) => Km.Create(a, a + 1);")]
    public async Task The_RASK127_fix_turns_new_into_Create(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix127(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static Km Longer(Km k) => k with { End = 5 };", "public static Km Longer(Km k) => Km.Create(k.Start, 5);")]
    [InlineData("public static Km Same(Km k) => k with { };", "public static Km Same(Km k) => Km.Create(k.Start, k.End);")]
    [InlineData("public static Km Leg(Trip t) => t.Leg with { Start = 0 };", "public static Km Leg(Trip t) => Km.Create(0, t.Leg.End);")]
    [InlineData("public static Plate Renamed(Plate p) => p with { };", "public static Plate Renamed(Plate p) => Plate.Create(p.Value);")]
    public async Task The_RASK127_fix_turns_a_with_into_Create_over_every_positional_member(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix127(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static Km Copy() => Make() with { End = 5 }; static Km Make() => Km.Create(1, 2);")]
    public async Task The_RASK127_fix_is_withheld_for_a_with_on_a_value_it_would_read_twice(string written)
    {
        var source = Source(written);

        var offered = await Offered127(source);

        Assert.False(offered);
    }

    [Theory]
    [InlineData("public record Plate(string Value) : Text(Value, MaxLength: 10);", "public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);")]
    [InlineData("public partial record Plate(string Value) : Text(Value, MaxLength: 10);", "public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);")]
    [InlineData("public sealed record Plate(string Value) : Text(Value, MaxLength: 10);", "public sealed partial record Plate(string Value) : Text(Value, MaxLength: 10);")]
    [InlineData("record Plate(int Start, int End) : Rask.Data.Range(Start, End);", "sealed partial record Plate(int Start, int End) : Rask.Data.Range(Start, End);")]
    public async Task The_RASK129_fix_adds_sealed_and_partial(string declaration, string expected)
    {
        var source = declaration;

        var fixed_ = await Fix129(source);

        Assert.Contains("\n" + expected + "\n", fixed_, StringComparison.Ordinal);
    }
}
