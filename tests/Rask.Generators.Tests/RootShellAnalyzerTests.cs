using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class RootShellAnalyzerTests
{
    // Minimal stubs whose full metadata names match the real entry points the analyzer keys on
    // (Rask.Server.RaskEndpointExtensions.UseRask<T>, Rask.Wasm.WasmHostBuilder.RunAsync<T>), so
    // the tests don't need to reference the host assemblies. The shell factories (Doctype/Html/
    // Head/Body) are matched by name, so the App declares same-named local helpers.
    private const string EntryStubs = """
                                      using Rask.Core;
                                      namespace Rask.Server { public static class RaskEndpointExtensions {
                                          public static void UseRask<TApp>(object app) where TApp : Component { } } }
                                      namespace Rask.Wasm { public sealed class WasmHostBuilder {
                                          public void RunAsync<TApp>() where TApp : Component { } } }
                                      """;

    private static string App(string renderBody) => $$"""
                                                      using Rask.Core;
                                                      namespace Demo;
                                                      public sealed partial class App : Component
                                                      {
                                                          private static object Doctype() => null!;
                                                          private static object Html(string lang) => null!;
                                                          private static object Head() => null!;
                                                          private static object Body() => null!;
                                                          protected override Component? Render() { {{renderBody}} return this; }
                                                      }
                                                      """;

    [Fact]
    public async Task UseRask_RootRendersTheWholeShell_ReportsRask021()
    {
        var src = EntryStubs + App("Doctype(); Html(\"en\"); Head(); Body();")
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.UseRask<App>(null!); } } }";

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK021", d.Id);
        Assert.Contains("Doctype(), Html(), Head(), Body()", d.GetMessage());
        Assert.Contains("App", d.GetMessage());
    }

    [Fact]
    public async Task UseRask_RootRendersOnlyItsBody_NoDiagnostic()
    {
        var src = EntryStubs + App(string.Empty)
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.UseRask<App>(null!); } } }";

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    /// <summary>
    ///     Any part of the shell is enough — a root that opens a document and forgets to close it is
    ///     the same mistake, and the half-built page is harder to read than the whole one.
    /// </summary>
    [Fact]
    public async Task RunAsync_WasmEntry_RootRendersPartOfTheShell_ReportsRask021()
    {
        var src = EntryStubs + App("Body();")
                             + "namespace Demo { class Host { void M() { new Rask.Wasm.WasmHostBuilder().RunAsync<App>(); } } }";

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK021", d.Id);
        Assert.Contains("Body()", d.GetMessage());
        Assert.DoesNotContain("Doctype()", d.GetMessage());
    }

    [Fact]
    public async Task UnrelatedGenericCall_NoDiagnostic()
    {
        // A generic method named UseRask but NOT on the Rask entry type must be ignored.
        var src = App("Doctype();")
                  + "namespace Demo { static class Other { public static void UseRask<T>() { } } "
                  + "class Host { void M() { Other.UseRask<App>(); } } }";

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RootShellAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK021").ToImmutableArray();
    }
}
