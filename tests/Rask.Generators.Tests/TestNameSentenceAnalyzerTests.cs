using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public sealed class TestNameSentenceAnalyzerTests
{
    // The frameworks' attributes, declared here rather than referenced: the analyzer knows them by name.
    private const string Frameworks = """
        namespace Xunit { public class FactAttribute : System.Attribute { } public class TheoryAttribute : FactAttribute { } }
        namespace NUnit.Framework { public class TestAttribute : System.Attribute { } }
        namespace Microsoft.VisualStudio.TestTools.UnitTesting { public class TestMethodAttribute : System.Attribute { } }
        """;

    [Fact]
    public async Task A_fact_named_as_a_sentence_reports_nothing()
    {
        var source = Tests("[Xunit.Fact] public void Remember_loads_once_then_serves_from_the_cache() { }");

        var diagnostics = await Diagnostics(source, enabled: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_fact_named_after_its_method_reports_RASK116()
    {
        var source = Tests("[Xunit.Fact] public void GetOrCreate_RunsFactoryOnce() { }");

        var diagnostic = Assert.Single(await Diagnostics(source, enabled: true));

        Assert.Equal("RASK116", diagnostic.Id);
        Assert.Contains("fewer than three words", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[Xunit.Theory] public void Should_load_the_page_once(int x) { }", "scaffolding word \"Should\"")]
    [InlineData("[NUnit.Framework.Test] public void Cache_GetOrCreate_Factory_once() { }", "mostly names")]
    [InlineData("[Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod] public void Load_Test_page() { }", "mostly names")]
    public async Task Every_framework_s_test_is_held_to_the_rule(string member, string why)
    {
        var source = Tests(member);

        var diagnostic = Assert.Single(await Diagnostics(source, enabled: true));

        Assert.Contains(why, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_method_that_is_not_a_test_is_left_alone()
    {
        var source = Tests("public void Helper() { }");

        var diagnostics = await Diagnostics(source, enabled: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_rule_is_off_until_a_strict_app_switches_it_on()
    {
        var source = Tests("[Xunit.Fact] public void GetOrCreate() { }");

        var diagnostics = await Diagnostics(source, enabled: false);

        Assert.Empty(diagnostics);
    }

    private static string Tests(string member) => Frameworks + $$"""

        namespace Demo.Tests
        {
            public sealed class CacheTests
            {
                {{member}}
            }
        }
        """;

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source, bool enabled)
    {
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        if (enabled)
        {
            options = options.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>(StringComparer.Ordinal) { ["RASK116"] = ReportDiagnostic.Warn });
        }

        var compilation = CSharpCompilation.Create(
            "Demo.Tests",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            GeneratorDriverFixture.BuildReferences(),
            options);

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TestNameSentenceAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        return [.. all.Where(d => d.Id == "RASK116")];
    }
}
