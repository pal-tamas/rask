using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

// RASKSUP001 — CS0108 is noise when the member being hidden is a builder entry, and meaningful when it
// is not. Both halves are asserted here: the suppressor exists to stop a framework naming collision
// from costing a keyword in the user's source, NOT to stop the compiler warning about a real one.
public class HiddenBuilderEntrySuppressorTests
{
    // `Summary`, `Label`, `Address` and `Filter` are HTML/SVG tags, so each has an entry on RaskMarkup
    // that every component inherits. Hiding one is what ~100 members in this repository do by accident.
    [Theory]
    [InlineData("public Component? Summary => null;")]
    [InlineData("public required string Label { get; set; }")]
    [InlineData("private Component? Details() => null;")]
    [InlineData("public sealed record Address(string Line);")]
    [InlineData("private readonly string Title = \"\";")]
    [InlineData("private static string Filter(string? s) => s ?? \"\";")]
    // `Form` and `Select` are GENERIC components, whose entry is a `RaskSeed_*` field rather than a
    // Build<T> member — a shape the shared entry test does not answer for. Nine members inside
    // Rask.Core alone hide one, so a suppressor that missed it would be visibly half-done.
    [InlineData("public string? Form { get; set; }")]
    [InlineData("public string? Select { get; set; }")]
    public async Task A_member_that_hides_a_tag_entry_has_its_CS0108_suppressed(string hider)
    {
        var cs0108 = await Cs0108Async($$"""
            using Rask.Core;
            namespace Demo;
            public class Modal : Component
            {
                {{hider}}
            }
            """);

        Assert.True(cs0108.FiredRaw, $"`{hider}` should have hidden an entry at all.");
        Assert.False(cs0108.Survives, $"CS0108 for `{hider}` should have been suppressed.");
    }

    [Fact]
    public async Task Hiding_a_real_member_of_the_users_own_base_component_still_warns()
    {
        // The narrowness that earns the suppression: `Count` is nobody's tag and nobody's component, so
        // this is an ordinary hiding mistake inside a component and the compiler should still say so.
        var cs0108 = await Cs0108Async("""
            using Rask.Core;
            namespace Demo;
            public class Panel : Component { public int Count => 1; }
            public class Wide : Panel { public int Count => 2; }
            """);

        Assert.True(cs0108.FiredRaw);
        Assert.True(cs0108.Survives);
    }

    [Fact]
    public async Task Hiding_outside_a_component_still_warns()
    {
        var cs0108 = await Cs0108Async("""
            namespace Demo;
            public class Base { public int Count => 1; }
            public class Derived : Base { public int Count => 2; }
            """);

        Assert.True(cs0108.FiredRaw);
        Assert.True(cs0108.Survives);
    }

    // Two-sided on purpose. "No CS0108 in the analyzer run" alone is the weaker claim — it reads the
    // same whether the warning was suppressed or never fired at all — so every case also asserts that
    // the raw compilation DID produce it. GetAllDiagnosticsAsync is what runs suppressors over the
    // compiler's own diagnostics, and it drops the ones they suppress.
    private static async Task<(bool FiredRaw, bool Survives)> Cs0108Async(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var firedRaw = compilation.GetDiagnostics().Any(d => d.Id == "CS0108");

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new HiddenBuilderEntrySuppressor()))
            .GetAllDiagnosticsAsync();

        return (firedRaw, all.Any(d => d.Id == "CS0108" && !d.IsSuppressed));
    }
}
