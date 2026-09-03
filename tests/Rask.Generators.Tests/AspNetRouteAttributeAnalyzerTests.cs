using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK067: a Rask component wearing ASP.NET's <c>[Route]</c> rather than Rask's.
/// </summary>
/// <remarks>
///     The real attributes, not stand-ins. Both live in the ASP.NET Core shared framework, which the
///     test compilation already references through Rask.Server — and using the genuine types is the
///     point, because the whole diagnostic is a claim about two specific shipping symbols. A stand-in
///     declared in the test source would still pass if Microsoft renamed or resealed either one.
/// </remarks>
public class AspNetRouteAttributeAnalyzerTests
{
    [Fact]
    public async Task AnMvcRouteOnAComponent_ReportsRask067()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using Rask.Core;

                                                    [Microsoft.AspNetCore.Mvc.Route("/orders")]
                                                    public sealed class Orders : Component
                                                    {
                                                        protected override Component? Render() => null;
                                                    }
                                                    """);

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("Orders", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.Mvc.RouteAttribute", d.GetMessage(), StringComparison.Ordinal);
        // The house shape: problem — remedy. Naming only the problem leaves the reader to guess that a
        // second, differently-namespaced Route exists at all, which is the entire confusion.
        Assert.Contains(" — ", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Rask.Core.Routing", d.GetMessage(), StringComparison.Ordinal);
    }

    // The same trap from the other direction, and the likelier one: someone arriving from Blazor types
    // the attribute they have always typed.
    [Fact]
    public async Task ABlazorRouteOnAComponent_ReportsRask067()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using Rask.Core;

                                                    [Microsoft.AspNetCore.Components.Route("/orders")]
                                                    public sealed class Orders : Component
                                                    {
                                                        protected override Component? Render() => null;
                                                    }
                                                    """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("Microsoft.AspNetCore.Components.RouteAttribute", d.GetMessage(), StringComparison.Ordinal);
    }

    // MVC's attribute is not sealed, so a project-local alias deriving from it is a real shape — and it
    // is exactly as invisible to Rask's router as the original. Matching on the name alone would miss it.
    [Fact]
    public async Task AnAliasDerivedFromMvcsRoute_ReportsRask067()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using Rask.Core;

                                                    public sealed class ApiRouteAttribute(string template)
                                                        : Microsoft.AspNetCore.Mvc.RouteAttribute(template);

                                                    [ApiRoute("/orders")]
                                                    public sealed class Orders : Component
                                                    {
                                                        protected override Component? Render() => null;
                                                    }
                                                    """);

        var d = Assert.Single(diagnostics);
        Assert.Contains("Orders", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RasksOwnRoute_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using Rask.Core;
                                                    using Rask.Core.Routing;

                                                    [Route("/orders")]
                                                    public sealed class Orders : Component
                                                    {
                                                        protected override Component? Render() => null;
                                                    }
                                                    """);

        Assert.Empty(diagnostics);
    }

    // Both attributes: the page DOES register, through Rask's. The ASP.NET one is inert, and failing a
    // build that is producing the correct route table would be a worse outcome than the stray attribute.
    [Fact]
    public async Task BothAttributesTogether_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    using Rask.Core;
                                                    using Rask.Core.Routing;

                                                    [Route("/orders")]
                                                    [Microsoft.AspNetCore.Mvc.Route("/orders")]
                                                    public sealed class Orders : Component
                                                    {
                                                        protected override Component? Render() => null;
                                                    }
                                                    """);

        Assert.Empty(diagnostics);
    }

    // The negative case that decides whether the analyzer is usable at all. A Rask server project is
    // an ASP.NET project: it may hold genuine controllers, and firing on those would make the rule
    // something people switch off rather than something they act on.
    [Fact]
    public async Task AnMvcRouteOnAnOrdinaryClass_ReportsNothing()
    {
        var diagnostics = await GetDiagnosticsAsync("""
                                                    [Microsoft.AspNetCore.Mvc.Route("/api/orders")]
                                                    public sealed class OrdersController
                                                    {
                                                        public string Get() => "ok";
                                                    }
                                                    """);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // The attributes under test have to RESOLVE, or every case here passes by binding to an error
        // symbol the analyzer correctly ignores — a whole file of green tests asserting nothing.
        Assert.False(
            compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
            "the test source must compile, or the analyzer is being asked about error symbols: "
            + string.Join("; ", compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new AspNetRouteAttributeAnalyzer()));
        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK067").ToImmutableArray();
    }
}
