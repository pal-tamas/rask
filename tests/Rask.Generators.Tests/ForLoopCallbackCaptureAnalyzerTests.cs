using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK120 — an event callback built in a <c>for</c> loop that reads the loop's one variable, so every row's
///     handler sees the value the loop ended on.
/// </summary>
public class ForLoopCallbackCaptureAnalyzerTests
{
    // ---------- it fires ----------

    [Fact]
    public async Task An_OnClick_built_in_a_for_loop_that_reads_its_variable_is_reported()
    {
        var loop = "for (var i = 0; i < _items.Count; i++) { rows.Add(Button.OnClick(() => _items.RemoveAt(i))[\"x\"]); }";

        var d = Assert.Single(await AnalyzeAsync(loop));

        Assert.Equal("RASK120", d.Id);
        Assert.Contains("'i'", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'OnClick'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_callback_reading_the_variable_twice_is_reported_once()
    {
        var loop = "for (var i = 0; i < 3; i++) { rows.Add(Button.OnClick(() => _picked = i + i)[\"x\"]); }";

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Equal("RASK120", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task An_async_callback_in_a_loop_counting_down_is_reported()
    {
        var loop = """
            for (var i = 3; i > 0; i -= 1)
            {
                rows.Add(Button.OnClick(async () => { await Task.Yield(); _picked = i; })["x"]);
            }
            """;

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Equal("RASK120", Assert.Single(diagnostics).Id);
    }

    // ---------- it stays quiet ----------

    [Fact]
    public async Task A_callback_reading_a_copy_taken_inside_the_loop_is_not_reported()
    {
        var loop = "for (var i = 0; i < 3; i++) { var row = i; rows.Add(Button.OnClick(() => _picked = row)[\"x\"]); }";

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_callback_built_in_a_foreach_is_not_reported()
    {
        var loop = "foreach (var i in new[] { 1, 2, 3 }) { rows.Add(Button.OnClick(() => _picked = i)[\"x\"]); }";

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_lambda_run_on_the_spot_in_a_for_loop_is_not_reported()
    {
        var loop = "for (var i = 0; i < 3; i++) { _picked += _items.Count(x => x > i); }";

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_lambda_handed_to_a_helper_that_is_not_a_callback_step_is_not_reported()
    {
        var loop = "for (var i = 0; i < 3; i++) { Later(() => _picked = i); }";

        var diagnostics = await AnalyzeAsync(loop);

        Assert.Empty(diagnostics);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string loop) =>
        CompiledAnalysis.RunAsync(
            BuilderGeneratorHarness.Compile($$"""
                using System;
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading.Tasks;
                using Rask.Core;
                namespace Demo
                {
                    public sealed partial class Page : Component
                    {
                        private readonly List<int> _items = [1, 2, 3];
                        private int _picked;

                        private static void Later(Action action) => _ = action;

                        protected override Component? Render()
                        {
                            var rows = new List<Component>();
                            {{loop}}
                            return Div[rows];
                        }
                    }
                }
                """),
            new ForLoopCallbackCaptureAnalyzer());
}
