using Rask.Batteries.Generators;
using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK127 and RASK129 quick-fixes, applied and compared.</summary>
public class ValueObjectCodeFixTests
{
    // The generated half's constructor, written by hand: the analyzer runs without the generator.
    private static string Plate(string elsewhere) => $$"""
        using Rask.Data;
        namespace Shop;
        public sealed partial record Plate() : Text(MaxLength: 10)
        {
            private Plate(string value) : this() => Value = value;
            public static Plate Create(string? value, string? field = null) => new(value ?? "");
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

    [Fact]
    public async Task The_RASK127_fix_turns_new_into_Create()
    {
        var fixed_ = await Fix127(Plate("public static Plate Make(string v) => new Plate(v);"));

        Assert.Contains("public static Plate Make(string v) => Plate.Create(v);", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK127_fix_names_the_type_of_a_target_typed_new()
    {
        var fixed_ = await Fix127(Plate("public static Plate Make(string v) => new(v);"));

        Assert.Contains("public static Plate Make(string v) => Plate.Create(v);", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK127_fix_is_withheld_for_with_and_for_a_new_with_no_value()
    {
        Assert.False(await Offered127(Plate("public static Plate Copy(Plate p) => p with { };")));
        Assert.False(await Offered127(Plate("public static Plate Make() => new Plate();")));
    }

    [Theory]
    [InlineData("public record Plate() : Text(MaxLength: 10);", "public sealed partial record Plate() : Text(MaxLength: 10);")]
    [InlineData("public partial record Plate() : Text(MaxLength: 10);", "public sealed partial record Plate() : Text(MaxLength: 10);")]
    [InlineData("public sealed record Plate() : Text(MaxLength: 10);", "public sealed partial record Plate() : Text(MaxLength: 10);")]
    [InlineData("record Plate() : Text(MaxLength: 10);", "sealed partial record Plate() : Text(MaxLength: 10);")]
    public async Task The_RASK129_fix_adds_sealed_and_partial(string declaration, string expected)
    {
        var fixed_ = await Fix129(declaration);

        Assert.Contains("\n" + expected + "\n", fixed_, StringComparison.Ordinal);
    }
}
