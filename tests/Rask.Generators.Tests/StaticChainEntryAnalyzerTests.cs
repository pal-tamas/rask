using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK104 — a static field or static auto-property keeps a component its initializer built from a chain
///     entry, which makes it one render's positional slot shared by every page (#1201).
/// </summary>
/// <remarks>
///     <c>AnalyzeAsync</c> fails the test if its source stops compiling: a file with a compile error reports no
///     analyzer diagnostics at all, and would pass every "is not reported" case for the wrong reason.
/// </remarks>
public class StaticChainEntryAnalyzerTests
{
    private const string Components = """
        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public string? Note { get; set; }
            }
        }
        """;

    // ---------- it fires ----------

    [Fact]
    public async Task A_static_readonly_field_holding_a_bare_entry_is_reported()
    {
        var source = "private static readonly Rask.Core.Component Empty = Div;";

        var d = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("RASK104", d.Id);
        Assert.Contains("'Empty'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Div'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private static readonly Rask.Core.Component Panel = Div.Class(\"panel\")[Span[\"hi\"]];")]
    [InlineData("private static readonly Card Noted = Card.Note(\"a\");")]
    [InlineData("public static Rask.Core.Component Empty { get; } = Div;")]
    [InlineData("private static Rask.Core.Component[] Rows = [Div, Span];")]
    [InlineData("private static readonly Rask.Core.Component Picked = System.DateTime.Now.Second > 30 ? Div : Span;")]
    public async Task A_static_member_initialised_from_a_chain_is_reported(string member)
    {
        var diagnostics = await AnalyzeAsync(member);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK104", d.Id);
    }

    [Fact]
    public async Task A_static_member_of_a_plain_class_is_reported_too()
    {
        var source = """
            namespace Demo
            {
                public static class Shared
                {
                    public static readonly Rask.Core.Component Empty = Rask.Markup.Div;
                }
            }
            """;

        var diagnostics = await AnalyzeSourceAsync(Components + "\n" + source);

        Assert.Equal("RASK104", Assert.Single(diagnostics).Id);
    }

    // ---------- it stays quiet ----------

    [Theory]
    [InlineData("private static Rask.Core.Component Empty => Div;")]
    [InlineData("private static Rask.Core.Component Empty() => Div;")]
    [InlineData("private static Rask.Core.Component Empty { get { return Div; } }")]
    [InlineData("private readonly Rask.Core.Component _empty = Div;")]
    [InlineData("private static readonly System.Func<Rask.Core.Component> Make = () => Div;")]
    [InlineData("private static readonly Rask.Core.Component? Nothing = null;")]
    [InlineData("private static readonly string Tag = \"div\";")]
    public async Task A_member_that_builds_per_call_or_holds_no_entry_is_not_reported(string member)
    {
        var diagnostics = await AnalyzeAsync(member);

        Assert.Empty(diagnostics);
    }

    // ---------- the quick-fix ----------

    [Fact]
    public async Task The_fix_turns_a_static_readonly_field_into_an_expression_bodied_property()
    {
        var source = Page("private static readonly Component Empty = Div;");

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new StaticChainEntryAnalyzer(), new StaticChainEntryCodeFixProvider(), "RASK104", source);

        Assert.Contains("private static Component Empty => Div;", fixhed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_turns_a_get_only_auto_property_into_an_expression_bodied_one()
    {
        var source = Page("public static Component Empty { get; } = Div.Class(\"x\");");

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new StaticChainEntryAnalyzer(), new StaticChainEntryCodeFixProvider(), "RASK104", source);

        Assert.Contains("public static Component Empty => Div.Class(\"x\");", fixhed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_is_withheld_from_a_writable_field()
    {
        var source = Page("private static Component Empty = Div;");

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new StaticChainEntryAnalyzer(), new StaticChainEntryCodeFixProvider(), "RASK104", source);

        Assert.False(offered);
    }

    private static string Page(string member) => $$"""
        using Rask.Core;
        namespace Demo;
        public sealed partial class App : Component
        {
            {{member}}

            protected override Component? Render() => Empty;
        }
        """;

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string member)
    {
        var page = $$"""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    {{member}}

                    protected override Rask.Core.Component? Render() => null;
                }
            }
            """;

        return AnalyzeSourceAsync(Components + "\n" + page);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeSourceAsync(string source)
    {
        // The compilation AFTER the factory generator has run, so the chain binds to generated entries.
        var compilation = BuilderGeneratorHarness.Compile(source);

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new Xunit.Sdk.XunitException(
                "The test source does not compile, so no analyzer result means anything:\n  "
                + string.Join("\n  ", errors.Select(e => e.ToString())));
        }

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StaticChainEntryAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }
}
