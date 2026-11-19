using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>RASK126 — lazy-loading proxies on a context that never allowed lazy loading.</summary>
/// <remarks>
///     The proxies package is not in this repo, so the source declares <c>ProxiesExtensions</c> where EF Core keeps it:
///     the analyzer matches the type by its full name, as it would the real one.
/// </remarks>
public class LazyLoadingProxiesAnalyzerTests
{
    private static string Program(string body) => $$"""
        namespace Microsoft.EntityFrameworkCore
        {
            public class Options { }

            public static class ProxiesExtensions
            {
                public static Options UseLazyLoadingProxies(this Options options) => options;
            }
        }

        namespace App
        {
            using Microsoft.EntityFrameworkCore;

            public static class RaskLike
            {
                public static Options AllowLazyLoading(this Options options) => options;
            }

            public static class Program
            {
                public static void Configure(Options o)
                {
                    {{body}}
                }
            }
        }
        """;

    [Fact]
    public async Task Proxies_without_the_opt_in_report_RASK126()
    {
        var diagnostics = await Diagnostics(Program("o.UseLazyLoadingProxies();"));

        Assert.Equal("RASK126", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Proxies_with_the_opt_in_in_the_same_chain_report_nothing()
    {
        var diagnostics = await Diagnostics(Program("o.UseLazyLoadingProxies().AllowLazyLoading();"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Proxies_with_the_opt_in_in_the_same_body_report_nothing()
    {
        var diagnostics = await Diagnostics(Program("""
            o.AllowLazyLoading();
            o.UseLazyLoadingProxies();
            """));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_method_of_the_same_name_elsewhere_reports_nothing()
    {
        var diagnostics = await Diagnostics("""
            public static class Mine
            {
                public static void UseLazyLoadingProxies(this object o) { }

                public static void Run() => new object().UseLazyLoadingProxies();
            }
            """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "LazyApp",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LazyLoadingProxiesAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
