using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class SyncAsyncHandlerAnalyzerTests
{
    // Wraps a Render() body alongside a component that still declares a sync/async PAIR.
    //
    // It has to be a local one now. The DOM events these tests used to drive — OnClick, OnScroll — are a
    // single `Callback` property each, so "both set" is not expressible on them any more and there is
    // nothing for this rule to find. What is left in its scope is the form and kit callbacks that are
    // still declared as pairs, and a component declared right here pins the rule rather than whichever
    // of those happens to survive next.
    private static string App(string body) => $$"""
                                                using System;
                                                using System.Collections.Generic;
                                                using System.Threading.Tasks;
                                                using Rask.Core;
                                                namespace Demo;
                                                public sealed partial class Widget : Component
                                                {
                                                    public Action? OnSave { get; set; }
                                                    public Func<Task>? OnSaveAsync { get; set; }
                                                    public Action<string>? OnPick { get; set; }
                                                    public Func<string, Task>? OnPickAsync { get; set; }
                                                    protected override Component? Render() => null;
                                                }
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
            "return Widget.OnSave(() => {}).OnSaveAsync(async () => await Task.Yield());")));
        Assert.Equal("RASK027", d.Id);
        Assert.Contains("OnSave", d.GetMessage());
        Assert.Contains("OnSaveAsync", d.GetMessage());
    }

    [Fact]
    public async Task ChainOnlyAsync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "return Widget.OnSaveAsync(async () => await Task.Yield());")));

    [Fact]
    public async Task OnlySync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App("return Widget.OnSave(() => {});")));

    [Fact]
    public async Task OnlyAsync_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "return Widget.OnSaveAsync(async () => await Task.Yield());")));

    [Fact]
    public async Task AsyncWithNullSync_NoDiagnostic() =>
        // Passing null for the sibling is the deliberate "set at most one" conditional shape.
        Assert.Empty(await Diagnostics(App(
            "return Widget.OnSave(null).OnSaveAsync(async () => await Task.Yield());")));

    [Fact]
    public async Task BothSyncAndAsyncTypedArg_ReportsRask027() =>
        Assert.Equal("RASK027", Assert.Single(await Diagnostics(App(
            "return Widget.OnPick(v => {}).OnPickAsync(async v => await Task.Yield());"))).Id);

    [Fact]
    public async Task DifferentEvents_NoDiagnostic() =>
        // OnSave (sync) + OnPickAsync (async) are different callbacks — not a conflict.
        Assert.Empty(await Diagnostics(App(
            "return Widget.OnSave(() => {}).OnPickAsync(async v => await Task.Yield());")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // The component under test is declared HERE, so its steps only exist once the builder generator
        // has run over this compilation — the referenced assemblies carry entries for their own
        // components, not for one written in the test source.
        compilation = (CSharpCompilation)GeneratorDriverFixture.WithBuilderSurface(compilation);

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new SyncAsyncHandlerAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK027").ToImmutableArray();
    }
}
