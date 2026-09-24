using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class RootShellAnalyzerTests
{
    // Minimal stubs whose full metadata names match the real entry points the analyzer keys on
    // (Rask.Server.RaskEndpointExtensions.MapRask<T>, Rask.Wasm.WasmHostBuilder.RunAsync<T>), so
    // the tests don't need to reference the host assemblies. The shell factories (Doctype/Html/
    // Head/Body) are matched by name, so the App declares same-named local helpers.
    private const string EntryStubs = """
                                      using Rask.Core;
                                      namespace Rask.Server { public static class RaskEndpointExtensions {
                                          public static void MapRask<TApp>(object app) where TApp : Component { } } }
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
    public async Task A_MapRask_root_that_renders_the_whole_shell_reports_RASK021()
    {
        var src = EntryStubs + App("Doctype(); Html(\"en\"); Head(); Body();")
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.MapRask<App>(null!); } } }";

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK021", d.Id);
        Assert.Contains("Doctype(), Html(), Head(), Body()", d.GetMessage());
        Assert.Contains("App", d.GetMessage());
    }

    // The chain spelling of the same mistake. `Html[…]` is an element access on a bare entry, not an
    // invocation, so a name-matched scan over InvocationExpressionSyntax never sees it. Uses the REAL
    // Rask.Core entries (inherited from RaskMarkup, so they bind here) rather than the local stubs the
    // other cases declare — those stubs are methods, which is precisely what a chain is not.
    [Fact]
    public async Task A_MapRask_root_that_renders_the_shell_as_a_chain_reports_RASK021()
    {
        var src = EntryStubs + """
                               namespace Demo;
                               public sealed partial class ChainApp : Component
                               {
                                   protected override Component? Render() => Html[Body[Div["hi"]]];
                               }
                               """
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.MapRask<ChainApp>(null!); } } }";

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK021", d.Id);
        Assert.Contains("ChainApp", d.GetMessage());
    }

    // A LOCAL that happens to be called Body is not the page shell. The bare-identifier arm added for the
    // chain matches by name, so without a symbol check this reports RASK021 on ordinary code — and the
    // repo builds -warnaserror, so a false positive here breaks a build rather than merely nagging.
    [Fact]
    public async Task A_local_named_like_the_shell_in_a_MapRask_root_raises_no_diagnostic()
    {
        var src = EntryStubs + """
                               namespace Demo;
                               public sealed partial class LocalApp : Component
                               {
                                   protected override Component? Render()
                                   {
                                       var Body = "an email body";
                                       var Doctype = 1;
                                       return Div[Body, Doctype.ToString()];
                                   }
                               }
                               """
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.MapRask<LocalApp>(null!); } } }";

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    [Fact]
    public async Task A_MapRask_root_that_renders_only_its_body_raises_no_diagnostic()
    {
        var src = EntryStubs + App(string.Empty)
                             + "namespace Demo { class Host { void M() { Rask.Server.RaskEndpointExtensions.MapRask<App>(null!); } } }";

        Assert.Empty(await GetDiagnosticsAsync(src));
    }

    /// <summary>
    ///     Any part of the shell is enough — a root that opens a document and forgets to close it is
    ///     the same mistake, and the half-built page is harder to read than the whole one.
    /// </summary>
    [Fact]
    public async Task A_WASM_RunAsync_root_that_renders_part_of_the_shell_reports_RASK021()
    {
        var src = EntryStubs + App("Body();")
                             + "namespace Demo { class Host { void M() { new Rask.Wasm.WasmHostBuilder().RunAsync<App>(); } } }";

        var d = Assert.Single(await GetDiagnosticsAsync(src));
        Assert.Equal("RASK021", d.Id);
        Assert.Contains("Body()", d.GetMessage());
        Assert.DoesNotContain("Doctype()", d.GetMessage());
    }

    [Fact]
    public async Task An_unrelated_generic_call_named_MapRask_raises_no_diagnostic()
    {
        // A generic method named MapRask but NOT on the Rask entry type must be ignored.
        var src = App("Doctype();")
                  + "namespace Demo { static class Other { public static void MapRask<T>() { } } "
                  + "class Host { void M() { Other.MapRask<App>(); } } }";

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
