using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class RedundantStateHasChangedAnalyzerTests
{
    // Wraps class members in a real Component, with the genuine Rask.Core factories in scope so the
    // analyzer resolves real callback parameter symbols (Action/Func<Task>/AfterBind).
    private static string App(string members) => $$"""
                                                  using System.Collections.Generic;
                                                  using System.Linq.Expressions;
                                                  using Rask.Core;
                                                  using Rask.Core.Forms;
                                                  using static Rask.Core.Components.Generated;
                                                  namespace Demo;
                                                  public sealed partial class App : Component
                                                  {
                                                      {{members}}
                                                  }
                                                  """;

    [Fact]
    public async Task OnClick_StateHasChanged_ReportsRask026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => Button(OnClick: () => StateHasChanged())[\"x\"];")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("OnClick", d.GetMessage());
    }

    [Fact]
    public async Task OnChange_StateHasChanged_ReportsRask026()
    {
        // Qualified: the generic builder entry (Component.Input<T>) shadows the unqualified factory.
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => "
            + "Rask.Core.Components.Generated.Input<string>(OnChange: _ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("OnChange", d.GetMessage());
    }

    [Fact]
    public async Task AfterBind_StateHasChanged_ReportsRask026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "private string _name = \"\";"
            + "protected override Component? Render() => "
            + "Rask.Core.Components.Generated.Input(() => _name, AfterBind: _ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("AfterBind", d.GetMessage());
    }

    // The same anti-pattern on the builder surface: the callback's name is the setter's, since every
    // generated setter's parameter is called `value`.
    [Fact]
    public async Task BuilderSetter_AfterBind_StateHasChanged_ReportsRask026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "private string _name = \"\";"
            + "protected override Component? Render() => Input.Bind(() => _name).AfterBind(_ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("AfterBind", d.GetMessage());
    }

    [Fact]
    public async Task BuilderSetter_Click_StateHasChanged_ReportsRask026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => Button.OnClick(() => StateHasChanged())[\"x\"];")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("Click", d.GetMessage());
    }

    [Fact]
    public async Task AsyncCallback_StateHasChanged_ReportsRask026() =>
        Assert.Equal("RASK026", Assert.Single(await Diagnostics(App(
                "protected override Component? Render() => "
                + "Button(OnClickAsync: async () => { await System.Threading.Tasks.Task.Yield(); StateHasChanged(); })[\"x\"];")))
            .Id);

    [Fact]
    public async Task BareHandler_NoStateHasChanged_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "protected override Component? Render() => Button(OnClick: () => { })[\"x\"];")));

    [Fact]
    public async Task StateHasChangedInLifecycleMethod_NoDiagnostic() =>
        // Not inside a callback lambda — a background/lifecycle StateHasChanged is legitimate.
        Assert.Empty(await Diagnostics(App(
            "protected override void OnMount() => StateHasChanged();"
            + "protected override Component? Render() => Div()[\"x\"];")));

    [Fact]
    public async Task StateHasChangedOnAnotherComponent_NoDiagnostic() =>
        // Re-rendering a *different* component from a callback can be intentional — only self-calls flag.
        Assert.Empty(await Diagnostics(App(
            "private readonly App _other = null!;"
            + "protected override Component? Render() => Button(OnClick: () => _other.StateHasChanged())[\"x\"];")));

    [Fact]
    public async Task StateHasChangedInLambdaToUserHelperTakingCallback_NoDiagnostic() =>
        // A user method whose parameter happens to be typed Action carries no auto-re-render guarantee —
        // only generated component factories do. The StateHasChanged here may be genuinely required.
        Assert.Empty(await Diagnostics(App(
            "private static Component Wrap(Action cb) => Div()[Button(OnClick: cb)[\"x\"]];"
            + "protected override Component? Render() => Wrap(() => StateHasChanged());")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new RedundantStateHasChangedAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK026").ToImmutableArray();
    }
}
