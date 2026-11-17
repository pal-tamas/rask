using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK119 — a <c>StateHasChanged()</c> in <c>OnMount</c>/<c>OnUpdated</c> that no <c>await</c> follows: the
///     hook runs before the render, and an awaiting one is rendered again when it completes.
/// </summary>
public class LifecycleStateHasChangedAnalyzerTests
{
    // ---------- it fires ----------

    [Fact]
    public async Task A_StateHasChanged_in_an_OnMount_with_no_await_is_reported()
    {
        var hook = "protected override async Task OnMount() { _n = 1; StateHasChanged(); }";

        var d = Assert.Single(await AnalyzeAsync(hook));

        Assert.Equal("RASK119", d.Id);
        Assert.Contains("'OnMount'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_StateHasChanged_after_the_last_await_of_OnUpdated_is_reported()
    {
        var hook = "protected override async Task OnUpdated() { await Task.Delay(1); _n = 2; StateHasChanged(); }";

        var d = Assert.Single(await AnalyzeAsync(hook));

        Assert.Equal("RASK119", d.Id);
        Assert.Contains("'OnUpdated'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expression_bodied_OnMount_that_only_calls_StateHasChanged_is_reported()
    {
        var hook = "protected override async Task OnMount() => this.StateHasChanged();";

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Equal("RASK119", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task A_StateHasChanged_in_the_catch_after_an_awaited_load_is_reported()
    {
        var hook = """
            protected override async Task OnMount()
            {
                try { await Task.Delay(1); }
                catch (InvalidOperationException) { _n = -1; StateHasChanged(); }
            }
            """;

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Equal("RASK119", Assert.Single(diagnostics).Id);
    }

    // ---------- it stays quiet ----------

    [Fact]
    public async Task A_StateHasChanged_before_an_await_is_not_reported()
    {
        var hook = "protected override async Task OnMount() { _n = 1; StateHasChanged(); await Task.Delay(1); }";

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_StateHasChanged_in_a_loop_that_awaits_is_not_reported()
    {
        var hook = """
            protected override async Task OnMount()
            {
                for (var tick = 0; tick < 3; tick++)
                {
                    _n = tick;
                    StateHasChanged();
                    await Task.Delay(1);
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_StateHasChanged_in_a_timer_callback_started_by_OnMount_is_not_reported()
    {
        var hook = """
            private System.Threading.Timer? _timer;
            protected override async Task OnMount() =>
                _timer = new System.Threading.Timer(_ => { _n++; StateHasChanged(); }, null, 1000, 1000);
            """;

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_StateHasChanged_after_the_await_of_OnFirstRender_is_not_reported()
    {
        // An after-render hook that finishes without yielding is not rendered again, so the call can be needed.
        var hook = "protected override async Task OnFirstRender() { await Task.Delay(1); _n = 1; StateHasChanged(); }";

        var diagnostics = await AnalyzeAsync(hook);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_StateHasChanged_in_an_ordinary_method_is_not_reported()
    {
        var method = "private void Tick() { _n++; StateHasChanged(); }";

        var diagnostics = await AnalyzeAsync(method);

        Assert.Empty(diagnostics);
    }

    // ---------- the quick-fix ----------

    [Fact]
    public async Task The_RASK119_fix_deletes_the_redundant_call()
    {
        var source = Page("protected override async Task OnMount() { await Task.Delay(1); _n = 1; StateHasChanged(); }");

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new LifecycleStateHasChangedAnalyzer(), new RedundantStateHasChangedCodeFixProvider(), "RASK119", source);

        Assert.DoesNotContain("StateHasChanged();", fixedSource, StringComparison.Ordinal);
        Assert.Contains("_n = 1;", fixedSource, StringComparison.Ordinal);
    }

    private static string Page(string members) => $$"""
        using System;
        using System.Threading.Tasks;
        using Rask.Core;
        namespace Demo
        {
            public sealed partial class Page : Component
            {
                private int _n;

                {{members}}

                protected override Component? Render() => P[_n.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            }
        }
        """;

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string members) =>
        CompiledAnalysis.RunAsync(BuilderGeneratorHarness.Compile(Page(members)), new LifecycleStateHasChangedAnalyzer());
}
