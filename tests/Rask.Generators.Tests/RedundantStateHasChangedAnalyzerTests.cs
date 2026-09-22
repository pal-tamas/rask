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
                                                  namespace Demo;
                                                  public sealed partial class App : Component
                                                  {
                                                      {{members}}
                                                  }
                                                  """;

    [Fact]
    public async Task StateHasChanged_inside_an_OnClick_callback_is_reported_as_RASK026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => Button.OnClick(() => StateHasChanged())[\"x\"];")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("OnClick", d.GetMessage());
    }

    [Fact]
    public async Task StateHasChanged_inside_an_OnChange_callback_is_reported_as_RASK026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => "
            + "Input.Of<string>().OnChange(_ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("OnChange", d.GetMessage());
    }

    [Fact]
    public async Task StateHasChanged_inside_an_AfterBind_callback_is_reported_as_RASK026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "private string _name = \"\";"
            + "protected override Component? Render() => "
            + "Input.Bind(() => _name).AfterBind(_ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("AfterBind", d.GetMessage());
    }

    // The same anti-pattern on the builder surface: the callback's name is the setter's, since every
    // generated setter's parameter is called `value`.
    [Fact]
    public async Task StateHasChanged_inside_the_AfterBind_builder_setter_is_reported_as_RASK026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "private string _name = \"\";"
            + "protected override Component? Render() => Input.Bind(() => _name)"
            + ".AfterBind(_ => StateHasChanged());")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("AfterBind", d.GetMessage());
    }

    [Fact]
    public async Task StateHasChanged_inside_the_Click_builder_setter_is_reported_as_RASK026()
    {
        var d = Assert.Single(await Diagnostics(App(
            "protected override Component? Render() => Button.OnClick(() => StateHasChanged())[\"x\"];")));
        Assert.Equal("RASK026", d.Id);
        Assert.Contains("Click", d.GetMessage());
    }

    [Fact]
    public async Task StateHasChanged_inside_an_async_callback_is_reported_as_RASK026() =>
        Assert.Equal("RASK026", Assert.Single(await Diagnostics(App(
                "protected override Component? Render() => "
                + "Button.OnClick(async () => { await System.Threading.Tasks.Task.Yield(); StateHasChanged(); })[\"x\"];")))
            .Id);

    [Fact]
    public async Task A_bare_handler_without_StateHasChanged_raises_no_diagnostic() =>
        Assert.Empty(await Diagnostics(App(
            "protected override Component? Render() => Button.OnClick(() => { })[\"x\"];")));

    [Fact]
    public async Task StateHasChanged_in_a_lifecycle_method_raises_no_diagnostic() =>
        // Not inside a callback lambda — a background/lifecycle StateHasChanged is legitimate.
        Assert.Empty(await Diagnostics(App(
            "protected override async Task OnMount() => StateHasChanged();"
            + "protected override Component? Render() => Div()[\"x\"];")));

    [Fact]
    public async Task StateHasChanged_on_another_component_raises_no_diagnostic() =>
        // Re-rendering a *different* component from a callback can be intentional — only self-calls flag.
        Assert.Empty(await Diagnostics(App(
            "private readonly App _other = null!;"
            + "protected override Component? Render() => Button.OnClick(() => _other.StateHasChanged())[\"x\"];")));

    [Fact]
    public async Task StateHasChanged_in_a_lambda_passed_to_a_user_helper_taking_a_callback_raises_no_diagnostic() =>
        // A user method whose parameter happens to be typed Action carries no auto-re-render guarantee —
        // only generated component factories do. The StateHasChanged here may be genuinely required.
        Assert.Empty(await Diagnostics(App(
            "private static Component Wrap(Action cb) => Div[Button.OnClick(cb)[\"x\"]];"
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
