using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK037. The builder entries are members, and a member beats a <c>using</c> alias in simple-name
///     lookup, so an alias that shares a component's name silently stops working inside a component —
///     as CS1061 at the use site, which names neither the alias nor the entry. These pin that the
///     analyzer speaks up at the alias, and that it stays quiet for a name no entry claims.
/// </summary>
public class BuilderEntryAliasAnalyzerTests
{
    // One file: an alias, and a component carrying a builder entry named `Card`. `Bench` is the control
    // — a property of the same type whose name is NOT its type's, so it is not an entry. The component
    // type is nested in a class rather than sitting in a namespace so that the alias cannot also trip
    // CS0576 and muddy what is being asserted.
    private static string Source(string usings) => $$"""
        using Rask.Core;
        {{usings}}

        namespace Demo
        {
            public static class Tools { }

            public static class Widgets
            {
                public sealed class Card : Component { }
            }

            public sealed class Page : Component
            {
                public static Widgets.Card Card => null!;
                public static Widgets.Card Bench => null!;
            }
        }
        """;

    [Fact]
    public async Task Alias_ShadowedByAnEntry_ReportsRask037()
    {
        var d = Assert.Single(await Diagnostics(Source("using Card = Demo.Tools;")));
        Assert.Equal("RASK037", d.Id);
        Assert.Contains("'Card'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Demo.Page", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("CS1061", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alias_PointsAtTheAliasName_NotTheWholeDirective()
    {
        var d = Assert.Single(await Diagnostics(Source("using Card = Demo.Tools;")));
        var text = d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan);
        Assert.Equal("Card", text);
    }

    [Fact]
    public async Task Alias_NamedAfterANonEntryMember_NoDiagnostic() =>
        // `Bench` is a property of a component type, but its name is not its type's, so nothing hides it.
        Assert.Empty(await Diagnostics(Source("using Bench = Demo.Tools;")));

    [Fact]
    public async Task Alias_ThatNoEntryClaims_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(Source("using Toolbox = Demo.Tools;")));

    [Fact]
    public async Task GlobalAlias_ShadowedByAnEntry_ReportsRask037() =>
        Assert.Equal("RASK037",
            Assert.Single(await Diagnostics(Source("global using Card = Demo.Tools;"))).Id);

    [Fact]
    public async Task Alias_InAFileWithNoComponent_NoDiagnostic() =>
        Assert.Empty(await Diagnostics("""
            using Card = Demo.Tools;

            namespace Demo
            {
                public static class Tools { }
                public static class Report { public static object? Use() => null; }
            }
            """));

    [Fact]
    public async Task PlainUsing_IsNotAnAlias_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(Source("using System.Text;")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BuilderEntryAliasAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
