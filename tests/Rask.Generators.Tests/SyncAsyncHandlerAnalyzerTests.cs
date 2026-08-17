using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class SyncAsyncHandlerAnalyzerTests
{
    // Wraps a Render() body in a component so the analyzer resolves the genuine generated factories
    // (Button/Div/Input) from the referenced Rask.Core.
    private static string App(string body) => $$"""
                                                using System.Collections.Generic;
                                                using System.Threading.Tasks;
                                                using Rask.Core;
                                                using static Rask.Core.Components.Generated;
                                                using static Rask.Html.Components.Generated;
                                                namespace Demo;
                                                public sealed partial class App : Component
                                                {
                                                    protected override Component? Render()
                                                    {
                                                        {{body}}
                                                    }
                                                }
                                                """;

    [Fact]
    public async Task BothSyncAndAsyncClick_ReportsRask027()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Button(OnClick: () => {}, OnClickAsync: async () => await Task.Yield())[\"x\"];")));
        Assert.Equal("RASK027", d.Id);
        Assert.Contains("OnClick", d.GetMessage());
        Assert.Contains("OnClickAsync", d.GetMessage());
    }

    // The chain is what the framework teaches. A chain's steps are extension methods on Build<T>, not a
    // static Generated.Button(...), so the factory branch matched none of these and one of the two
    // handlers was silently dropped with nothing said.
    [Fact]
    public async Task ChainBothSyncAndAsyncClick_ReportsRask027()
    {
        var d = Assert.Single(await Diagnostics(App(
            "return Button.OnClick(() => {}).OnClickAsync(async () => await Task.Yield())[\"x\"];")));
        Assert.Equal("RASK027", d.Id);
        Assert.Contains("OnClick", d.GetMessage());
        Assert.Contains("OnClickAsync", d.GetMessage());
    }

    [Fact]
    public async Task ChainOnlyAsync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "return Button.OnClickAsync(async () => await Task.Yield())[\"x\"];")));

    [Fact]
    public async Task OnlySync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Button(OnClick: () => {})[\"x\"];")));

    [Fact]
    public async Task OnlyAsync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "return Button(OnClickAsync: async () => await Task.Yield())[\"x\"];")));

    [Fact]
    public async Task AsyncWithNullSync_NoDiagnostic() =>
        // Passing null for the sibling is the deliberate "set at most one" conditional shape.
        Assert.Empty(await Diagnostics(App(
            "return Button(OnClick: null, OnClickAsync: async () => await Task.Yield())[\"x\"];")));

    [Fact]
    public async Task BothSyncAndAsyncScroll_ReportsRask027() =>
        Assert.Equal("RASK027", Assert.Single(await Diagnostics(App(
            "return Div(OnScroll: e => {}, OnScrollAsync: async e => await Task.Yield())[\"x\"];"))).Id);

    [Fact]
    public async Task DifferentEvents_NoDiagnostic() =>
        // OnClick (sync) + OnScrollAsync (async) are different events — not a conflict.
        Assert.Empty(await Diagnostics(App(
            "return Div(OnClick: () => {}, OnScrollAsync: async e => await Task.Yield())[\"x\"];")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SyncAsyncHandlerAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK027").ToImmutableArray();
    }
}
